using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Posts payments against payables. One committed payment allocates its full amount to open
/// purchase invoices and commits one balanced journal (Dr AP / Cr Bank or Cash) through the
/// finance kernel. Allocations are checked under a per-company advisory lock, so two concurrent
/// payments can never over-allocate an invoice. Customer receipts arrive with the sales/AR slice.
/// </summary>
public sealed class PaymentService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    ILogger<PaymentService> logger) : IPaymentService
{
    /// <summary>Unique idempotency index; duplicate submissions classify against this name.</summary>
    public const string RequestKeyIndex = "UX_PaymentEntries_RequestKey";

    public const string PaymentDocumentType = "PaymentEntry";

    private const int MaximumAllocations = 200;

    /// <inheritdoc />
    public async Task<PaymentResult> PostPaymentAsync(
        PostPaymentRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateRequestShape(request);
        var normalized = Normalize(request);
        var requestHash = ComputeRequestHash(normalized);

        PaymentResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"payment:{normalized.CompanyId}", cancellationToken);

                var committed = await FindCommittedAsync(normalized.CompanyId, normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This payment request key was already posted with a different payload.");
                    result = await ToResultAsync(committed, replayed: true, cancellationToken);
                    return;
                }

                result = await PostCoreAsync(normalized, requestHash, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () =>
                await FindCommittedAsync(normalized.CompanyId, normalized.RequestKey, cancellationToken) is not null);

        return result;
    }

    /// <inheritdoc />
    public Task<PaymentEntry?> GetPaymentAsync(
        int companyId,
        int paymentEntryId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.PaymentEntries
            .Include(payment => payment.Allocations.OrderBy(allocation => allocation.LineNumber))
            .SingleOrDefaultAsync(payment => payment.Id == paymentEntryId && payment.CompanyId == companyId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PaymentEntry>> ListPaymentsAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await context.PaymentEntries
            .Where(payment => payment.CompanyId == companyId)
            .OrderByDescending(payment => payment.PostedAtUtc)
            .ThenByDescending(payment => payment.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<PaymentResult> PostCoreAsync(
        PostPaymentRequest request,
        string requestHash,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await context.Companies.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CompanyId && candidate.IsActive, cancellationToken)
            ?? throw new FinancePostingValidationException("Active company not found.");

        string partyLabel;
        if (request.PaymentType == PaymentEntryType.InternalTransfer)
        {
            partyLabel = "internal transfer";
            if (request.Allocations.Count > 0)
                throw new FinancePostingValidationException("Internal transfers carry no allocations.");
            if (request.FromAccountId is not { } fromAccountId || request.ToAccountId is not { } toAccountId)
                throw new FinancePostingValidationException(
                    "An internal transfer requires FromAccountId and ToAccountId.");
            if (fromAccountId == toAccountId)
                throw new FinancePostingValidationException(
                    "An internal transfer must move money between two different accounts.");
        }
        else if (request.PaymentType == PaymentEntryType.Pay)
        {
            if (request.SupplierId is not { } supplierId || supplierId <= 0)
                throw new FinancePostingValidationException("A supplier payment requires a SupplierId.");
            var supplier = await context.Suppliers.SingleOrDefaultAsync(
                    candidate => candidate.Id == supplierId, cancellationToken)
                ?? throw new FinancePostingValidationException("Supplier not found.");
            partyLabel = supplier.Name;
        }
        else
        {
            if (request.CustomerId is not { } customerId || customerId <= 0)
                throw new FinancePostingValidationException("A customer receipt requires a CustomerId.");
            var customer = await context.Customers.SingleOrDefaultAsync(
                    candidate => candidate.Id == customerId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Customer not found in this company.");
            partyLabel = customer.Name;
        }

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        if (request.PaymentType == PaymentEntryType.InternalTransfer)
        {
            await RequirePostingAccountsAsync(request.CompanyId, request.FromAccountId!.Value, cancellationToken);
            await RequirePostingAccountsAsync(request.CompanyId, request.ToAccountId!.Value, cancellationToken);
        }
        else if (!request.IsAdvance)
        {
            // Open value derives from posted invoices minus posted allocations minus party
            // advances, including this request's own allocations; the advisory lock serializes
            // the check. For receipts the allocation's invoice id names the sales invoice.
            await ValidateAllocationsAsync(request, cancellationToken);
        }

        if (request.IsAdvance && request.Allocations.Count > 0)
            throw new FinancePostingValidationException(
                "An advance posts without allocations; allocate the advance to invoices in a later payment.");
        if (request.IsAdvance && request.PaymentType == PaymentEntryType.InternalTransfer)
            throw new FinancePostingValidationException("Internal transfers are never advances.");

        var totalAllocated = request.Allocations.Sum(allocation => allocation.Amount);
        if (!request.IsAdvance && request.PaymentType != PaymentEntryType.InternalTransfer && totalAllocated != request.Amount)
            throw new FinancePostingValidationException(
                $"The payment amount {request.Amount} must be fully allocated; allocations total {totalAllocated}. " +
                "Post advances with isAdvance=true to record unallocated money.");

        var payment = new PaymentEntry
        {
            CompanyId = request.CompanyId,
            PaymentType = request.PaymentType,
            Method = request.Method,
            SupplierId = request.PaymentType == PaymentEntryType.Pay ? request.SupplierId : null,
            CustomerId = request.PaymentType == PaymentEntryType.Receive ? request.CustomerId : null,
            IsAdvance = request.IsAdvance,
            FromAccountId = request.FromAccountId,
            ToAccountId = request.ToAccountId,
            Amount = request.Amount,
            CurrencyCode = company.BaseCurrency.ToUpperInvariant(),
            PostingDate = postingDate,
            Reference = request.Reference,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim(),
            Allocations = request.Allocations
                .Select((allocation, index) => new PaymentAllocation
                {
                    CompanyId = request.CompanyId,
                    LineNumber = index + 1,
                    PurchaseInvoiceId = request.PaymentType == PaymentEntryType.Pay
                        ? allocation.PurchaseInvoiceId
                        : null,
                    SalesInvoiceId = request.PaymentType == PaymentEntryType.Receive
                        ? allocation.PurchaseInvoiceId
                        : null,
                    Amount = allocation.Amount
                })
                .ToList()
        };
        context.PaymentEntries.Add(payment);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        var posted = request.PaymentType == PaymentEntryType.InternalTransfer
            ? await PostTransferJournalAsync(request, payment, company, postingDate, partyLabel, actor, cancellationToken)
            : await PostPartyJournalAsync(request, payment, company, postingDate, partyLabel, actor, cancellationToken);

        logger.LogInformation(
            "Payment {PaymentId} posted for company {CompanyId}: {Amount} {Direction} {PartyLabel}, journal {JournalNumber}",
            payment.Id, request.CompanyId, request.Amount,
            request.PaymentType == PaymentEntryType.Pay ? "to" : "from", partyLabel, posted.JournalNumber);

        return new PaymentResult(payment.Id, postingDate, request.Amount, totalAllocated, posted.JournalNumber, false);
    }

    private async Task<PaymentResult> ToResultAsync(
        PaymentEntry payment,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var journal = await context.JournalEntries.SingleOrDefaultAsync(
            entry => entry.SourceDocumentType == PaymentDocumentType &&
                entry.SourceDocumentId == payment.Id.ToString(CultureInfo.InvariantCulture) &&
                entry.SourceAction == "Post" &&
                entry.SourceVersion == 1,
            cancellationToken);
        return new PaymentResult(
            payment.Id,
            payment.PostingDate,
            payment.Amount,
            payment.Allocations.Sum(allocation => allocation.Amount),
            journal?.JournalNumber,
            replayed);
    }

    private async Task<JournalPostingResult> PostPartyJournalAsync(
        PostPaymentRequest request,
        PaymentEntry payment,
        Company company,
        DateOnly postingDate,
        string partyLabel,
        string actor,
        CancellationToken cancellationToken)
    {
        var tenderAccount = await postingPolicy.ResolveAsync(
            request.CompanyId,
            request.Method == PaymentMethod.Cash ? AccountPostingRole.CashOnHand : AccountPostingRole.Bank,
            cancellationToken);
        var counterAccount = await postingPolicy.ResolveAsync(
            request.CompanyId,
            request.PaymentType == PaymentEntryType.Pay
                ? AccountPostingRole.AccountsPayable
                : AccountPostingRole.AccountsReceivable,
            cancellationToken);
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? string.Empty : $" ({request.Reference})";
        var advanceLabel = request.IsAdvance ? "advance " : string.Empty;
        return await journalPosting.PostAsync(
            new JournalPostingRequest(
                request.CompanyId,
                postingDate,
                company.BaseCurrency,
                request.PaymentType == PaymentEntryType.Pay
                    ? $"Supplier payment {advanceLabel}to {partyLabel}{reference}"
                    : $"Customer receipt {advanceLabel}from {partyLabel}{reference}",
                PaymentDocumentType,
                payment.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                request.PaymentType == PaymentEntryType.Pay
                    ?
                    [
                        new JournalLineInput(counterAccount.ChartOfAccountId, $"Payable settlement {partyLabel}", request.Amount, 0m),
                        new JournalLineInput(tenderAccount.ChartOfAccountId, request.Method == PaymentMethod.Cash ? "Cash payment" : "Bank payment", 0m, request.Amount)
                    ]
                    :
                    [
                        new JournalLineInput(tenderAccount.ChartOfAccountId, request.Method == PaymentMethod.Cash ? "Cash receipt" : "Bank receipt", request.Amount, 0m),
                        new JournalLineInput(counterAccount.ChartOfAccountId, $"Receivable settlement {partyLabel}", 0m, request.Amount)
                    ]),
            actor,
            cancellationToken);
    }

    private async Task<JournalPostingResult> PostTransferJournalAsync(
        PostPaymentRequest request,
        PaymentEntry payment,
        Company company,
        DateOnly postingDate,
        string partyLabel,
        string actor,
        CancellationToken cancellationToken)
    {
        var fromAccount = await context.ChartOfAccounts.SingleAsync(
            account => account.Id == request.FromAccountId, cancellationToken);
        var toAccount = await context.ChartOfAccounts.SingleAsync(
            account => account.Id == request.ToAccountId, cancellationToken);
        return await journalPosting.PostAsync(
            new JournalPostingRequest(
                request.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"Internal transfer from {fromAccount.Name} to {toAccount.Name}" +
                    (string.IsNullOrWhiteSpace(request.Reference) ? string.Empty : $" ({request.Reference})"),
                PaymentDocumentType,
                payment.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                [
                    new JournalLineInput(toAccount.Id, $"Transfer in from {fromAccount.Name}", request.Amount, 0m),
                    new JournalLineInput(fromAccount.Id, $"Transfer out to {toAccount.Name}", 0m, request.Amount)
                ]),
            actor,
            cancellationToken);
    }

    private async Task ValidateAllocationsAsync(
        PostPaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Allocations.Any(allocation => allocation.Amount <= 0 || allocation.PurchaseInvoiceId <= 0))
            throw new FinancePostingValidationException(
                "Every allocation needs a positive invoice id and a positive amount.");

        var allocationGroups = request.Allocations
            .GroupBy(allocation => allocation.PurchaseInvoiceId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Amount));
        var invoiceIds = allocationGroups.Keys.ToList();
        var partyId = request.PaymentType == PaymentEntryType.Pay ? request.SupplierId : request.CustomerId;

        // Party-level balance: invoices minus allocations minus advances. An advance is money
        // already settled against the party, so it lowers what their invoices can still claim.
        var partyOpen = partyId is { } openPartyId
            ? await context.PaymentEntries
                .Where(payment => payment.CompanyId == request.CompanyId &&
                    payment.PaymentType == request.PaymentType &&
                    (request.PaymentType == PaymentEntryType.Pay
                        ? payment.SupplierId == openPartyId
                        : payment.CustomerId == openPartyId))
                .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m
            : 0m;
        var advanceTotal = partyId is { } advancePartyId
            ? await context.PaymentEntries
                .Where(payment => payment.CompanyId == request.CompanyId &&
                    payment.PaymentType == request.PaymentType &&
                    (request.PaymentType == PaymentEntryType.Pay
                        ? payment.SupplierId == advancePartyId
                        : payment.CustomerId == advancePartyId))
                .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m
            : 0m;
        var allocatedTotal = partyId is { } allocatedPartyId
            ? await context.PaymentAllocations
                .Where(allocation => allocation.CompanyId == request.CompanyId &&
                    (request.PaymentType == PaymentEntryType.Pay
                        ? allocation.PurchaseInvoice!.SupplierId == allocatedPartyId
                        : allocation.SalesInvoice!.CustomerId == allocatedPartyId))
                .SumAsync(allocation => (decimal?)allocation.Amount, cancellationToken) ?? 0m
            : 0m;

        if (request.PaymentType == PaymentEntryType.Pay)
        {
            var invoices = await context.PurchaseInvoices
                .Where(invoice => invoice.CompanyId == request.CompanyId && invoiceIds.Contains(invoice.Id))
                .ToDictionaryAsync(invoice => invoice.Id, cancellationToken);
            var priorPaid = await context.PaymentAllocations
                .Where(allocation => allocation.CompanyId == request.CompanyId &&
                    invoiceIds.Contains(allocation.PurchaseInvoiceId!.Value))
                .GroupBy(allocation => allocation.PurchaseInvoiceId!.Value)
                .Select(group => new { Id = group.Key, Paid = group.Sum(allocation => allocation.Amount) })
                .ToDictionaryAsync(row => row.Id, row => row.Paid, cancellationToken);
            await AssertWithinOpenAsync(allocationGroups, invoices
                .ToDictionary(pair => pair.Key, pair => (pair.Value.InvoiceNumber, pair.Value.TotalValue)),
                priorPaid, "purchase invoice");
        }
        else
        {
            var invoices = await context.SalesInvoices
                .Where(invoice => invoice.CompanyId == request.CompanyId && invoiceIds.Contains(invoice.Id))
                .ToDictionaryAsync(invoice => invoice.Id, cancellationToken);
            var priorPaid = await context.PaymentAllocations
                .Where(allocation => allocation.CompanyId == request.CompanyId &&
                    invoiceIds.Contains(allocation.SalesInvoiceId!.Value))
                .GroupBy(allocation => allocation.SalesInvoiceId!.Value)
                .Select(group => new { Id = group.Key, Paid = group.Sum(allocation => allocation.Amount) })
                .ToDictionaryAsync(row => row.Id, row => row.Paid, cancellationToken);
            await AssertWithinOpenAsync(allocationGroups, invoices
                .ToDictionary(pair => pair.Key, pair => (pair.Value.InvoiceNumber, pair.Value.TotalValue)),
                priorPaid, "sales invoice");
        }

        // Party-level cap across invoices and advances.
        var invoiceTotal = request.PaymentType == PaymentEntryType.Pay
            ? await context.PurchaseInvoices
                .Where(invoice => invoice.CompanyId == request.CompanyId && invoice.SupplierId == partyId)
                .SumAsync(invoice => (decimal?)invoice.TotalValue, cancellationToken) ?? 0m
            : await context.SalesInvoices
                .Where(invoice => invoice.CompanyId == request.CompanyId && invoice.CustomerId == partyId)
                .SumAsync(invoice => (decimal?)invoice.TotalValue, cancellationToken) ?? 0m;
        var partyBalance = invoiceTotal - allocatedTotal - advanceTotal;
        if (allocationGroups.Values.Sum() > partyBalance)
            throw new FinancePostingValidationException(
                $"Allocating {allocationGroups.Values.Sum()} exceeds the party's open balance of {partyBalance} " +
                "(invoices minus settled money and advances).");
    }

    private async Task RequirePostingAccountsAsync(
        int companyId,
        int accountId,
        CancellationToken cancellationToken)
    {
        var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
                candidate => candidate.Id == accountId && candidate.CompanyId == companyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException(
                $"Transfer account {accountId} does not exist in this company.");
        if (!account.IsActive || account.IsGroupAccount)
            throw new FinancePostingValidationException(
                "Transfer accounts must be active posting accounts.");
    }

    private static async Task AssertWithinOpenAsync(
        IReadOnlyDictionary<int, decimal> allocationGroups,
        IReadOnlyDictionary<int, (string InvoiceNumber, decimal TotalValue)> invoices,
        IReadOnlyDictionary<int, decimal> priorPaid,
        string documentLabel)
    {
        foreach (var (invoiceId, requested) in allocationGroups)
        {
            if (!invoices.TryGetValue(invoiceId, out var invoice))
                throw new FinancePostingValidationException(
                    $"The {documentLabel} {invoiceId} does not exist in this company.");
            var open = invoice.TotalValue - priorPaid.GetValueOrDefault(invoiceId);
            if (requested > open)
                throw new FinancePostingValidationException(
                    $"Allocating {requested} against {documentLabel} '{invoice.InvoiceNumber}' exceeds its " +
                    $"open balance of {open}; over-allocation is not permitted.");
        }

        await Task.CompletedTask;
    }

    private Task<PaymentEntry?> FindCommittedAsync(
        int companyId,
        string requestKey,
        CancellationToken cancellationToken) =>
        context.PaymentEntries.SingleOrDefaultAsync(
            payment => payment.CompanyId == companyId && payment.RequestKey == requestKey,
            cancellationToken);

    private static void ValidateRequestShape(PostPaymentRequest request)
    {
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        if (!Enum.IsDefined(request.PaymentType) || !Enum.IsDefined(request.Method))
            throw new FinancePostingValidationException("Payment type and method must be defined values.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException(
                "A payment request key of 1 to 200 characters is required.");
        if (request.Amount <= 0)
            throw new FinancePostingValidationException("The payment amount must be positive.");
        if (request.Amount != decimal.Round(request.Amount, 6, MidpointRounding.AwayFromZero))
            throw new FinancePostingValidationException(
                "The payment amount must already be rounded to at most 6 decimal places.");
        if (request.Allocations is null || request.Allocations.Count > MaximumAllocations)
            throw new FinancePostingValidationException(
                $"A payment carries at most {MaximumAllocations} allocations.");
        if (request.PaymentType == PaymentEntryType.InternalTransfer)
        {
            if (request.Allocations.Count > 0)
                throw new FinancePostingValidationException("Internal transfers carry no allocations.");
        }
        else if (!request.IsAdvance && request.Allocations.Count == 0)
        {
            throw new FinancePostingValidationException(
                "A payment requires at least one allocation, or post it with isAdvance=true.");
        }
        foreach (var allocation in request.Allocations)
        {
            if (allocation is null || allocation.PurchaseInvoiceId <= 0 || allocation.Amount <= 0)
                throw new FinancePostingValidationException(
                    "Every allocation needs a positive invoice id and a positive amount.");
        }
    }

    private static PostPaymentRequest Normalize(PostPaymentRequest request) => request with
    {
        RequestKey = request.RequestKey.Trim(),
        Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
    };

    private static string ComputeRequestHash(PostPaymentRequest request)
    {
        var canonical = new StringBuilder();
        void Append(string value) =>
            canonical.Append(value.Replace("\u001f", " ", StringComparison.Ordinal)).Append('\u001f');

        Append(request.CompanyId.ToString(CultureInfo.InvariantCulture));
        Append(((int)request.PaymentType).ToString(CultureInfo.InvariantCulture));
        Append(((int)request.Method).ToString(CultureInfo.InvariantCulture));
        Append(request.RequestKey);
        Append(request.Amount.ToString("0.########", CultureInfo.InvariantCulture));
        Append((request.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty);
        Append(request.Reference ?? string.Empty);
        Append(request.Notes ?? string.Empty);
        foreach (var allocation in request.Allocations)
        {
            Append(allocation.PurchaseInvoiceId.ToString(CultureInfo.InvariantCulture));
            Append(allocation.Amount.ToString("0.########", CultureInfo.InvariantCulture));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
