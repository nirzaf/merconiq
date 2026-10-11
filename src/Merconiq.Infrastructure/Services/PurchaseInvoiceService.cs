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
/// Posts supplier invoices matched to posted goods-receipt lines. One committed invoice clears
/// exactly the GRNI value receiving accrued and credits accounts payable for the same amount;
/// stock is never touched and every matched receipt line is consumed exactly once.
/// </summary>
public sealed class PurchaseInvoiceService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    ILogger<PurchaseInvoiceService> logger) : IPurchaseInvoiceService
{
    /// <summary>Unique idempotency index; duplicate submissions classify against this name.</summary>
    public const string RequestKeyIndex = "UX_PurchaseInvoices_RequestKey";

    /// <summary>One invoice line per posted receipt line, ever.</summary>
    public const string ReceiptLineMatchIndex = "UX_PurchaseInvoiceLines_ReceiptLine";

    public const string InvoiceDocumentType = "PurchaseInvoice";

    private const int MaximumLines = 200;

    /// <inheritdoc />
    public async Task<PurchaseInvoiceResult> PostPurchaseInvoiceAsync(
        PostPurchaseInvoiceRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateRequestShape(request);
        var normalized = Normalize(request);
        var requestHash = ComputeRequestHash(normalized);

        PurchaseInvoiceResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"purchase-invoice:{normalized.CompanyId}", cancellationToken);

                var committed = await FindCommittedAsync(normalized.CompanyId, normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This purchase-invoice request key was already posted with a different payload.");
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
    public Task<PurchaseInvoice?> GetPurchaseInvoiceAsync(
        int companyId,
        int purchaseInvoiceId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.PurchaseInvoices
            .Include(invoice => invoice.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(invoice => invoice.Id == purchaseInvoiceId && invoice.CompanyId == companyId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseInvoice>> ListPurchaseInvoicesAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await context.PurchaseInvoices
            .Where(invoice => invoice.CompanyId == companyId)
            .OrderByDescending(invoice => invoice.PostedAtUtc)
            .ThenByDescending(invoice => invoice.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PayablesOpenSummary> GetPayablesOpenAsync(
        int companyId,
        int? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var invoices = context.PurchaseInvoices.Where(invoice => invoice.CompanyId == companyId);
        if (supplierId is { } supplierFilter)
            invoices = invoices.Where(invoice => invoice.SupplierId == supplierFilter);

        var paidByInvoice = await context.PaymentAllocations
            .Where(allocation => allocation.CompanyId == companyId && allocation.PurchaseInvoiceId != null)
            .GroupBy(allocation => allocation.PurchaseInvoiceId!.Value)
            .Select(group => new { PurchaseInvoiceId = group.Key, Paid = group.Sum(allocation => allocation.Amount) })
            .ToDictionaryAsync(row => row.PurchaseInvoiceId, row => row.Paid, cancellationToken);

        var rows = await invoices
            .OrderBy(invoice => invoice.DueDate)
            .ThenBy(invoice => invoice.Id)
            .ToListAsync(cancellationToken);
        var items = rows.Select(invoice => new PayableOpenItem(
                invoice.Id,
                invoice.SupplierId,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.TotalValue,
                paidByInvoice.GetValueOrDefault(invoice.Id),
                invoice.TotalValue - paidByInvoice.GetValueOrDefault(invoice.Id)))
            .ToList();
        return new PayablesOpenSummary(companyId, null, items, items.Sum(item => item.OpenValue));
    }

    /// <inheritdoc />
    public async Task<AgingReport> GetPayablesAgingAsync(
        int companyId,
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var asOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var invoices = await context.PurchaseInvoices
            .Where(invoice => invoice.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var paidByInvoice = await context.PaymentAllocations
            .Where(allocation => allocation.CompanyId == companyId && allocation.PurchaseInvoiceId != null)
            .GroupBy(allocation => allocation.PurchaseInvoiceId!.Value)
            .Select(group => new { PurchaseInvoiceId = group.Key, Paid = group.Sum(allocation => allocation.Amount) })
            .ToDictionaryAsync(row => row.PurchaseInvoiceId, row => row.Paid, cancellationToken);
        var supplierNames = await context.Suppliers
            .ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);

        var open = invoices
            .Select(invoice => new
            {
                invoice.SupplierId,
                Open = invoice.TotalValue - paidByInvoice.GetValueOrDefault(invoice.Id),
                DaysPastDue = (asOf.DayNumber - invoice.DueDate.DayNumber)
            })
            .Where(row => row.Open > 0)
            .ToList();
        var rows = open
            .GroupBy(row => row.SupplierId)
            .Select(group => new AgingBucketRow(
                group.Key,
                supplierNames.GetValueOrDefault(group.Key, $"Supplier {group.Key}"),
                group.Sum(row => row.DaysPastDue <= 0 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 0 and <= 30 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 30 and <= 60 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue is > 60 and <= 90 ? row.Open : 0m),
                group.Sum(row => row.DaysPastDue > 90 ? row.Open : 0m),
                group.Sum(row => row.Open)))
            .OrderBy(row => row.TotalOpen)
            .ToList();
        return new AgingReport(companyId, asOf, "Payables", rows, rows.Sum(row => row.TotalOpen));
    }

    private async Task<PurchaseInvoiceResult> PostCoreAsync(
        PostPurchaseInvoiceRequest request,
        string requestHash,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await context.Companies.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CompanyId && candidate.IsActive, cancellationToken)
            ?? throw new FinancePostingValidationException("Active company not found.");

        var supplier = await context.Suppliers.SingleOrDefaultAsync(
                candidate => candidate.Id == request.SupplierId, cancellationToken)
            ?? throw new FinancePostingValidationException("Supplier not found.");

        var duplicate = await context.PurchaseInvoices.AnyAsync(
            invoice => invoice.CompanyId == request.CompanyId &&
                invoice.SupplierId == request.SupplierId &&
                invoice.InvoiceNumber == request.InvoiceNumber,
            cancellationToken);
        if (duplicate)
            throw new FinancePostingConflictException(
                $"An invoice numbered '{request.InvoiceNumber}' already exists for this supplier in this company.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var receiptLineIds = request.Lines.Select(line => line.GoodsReceiptLineId).Distinct().ToList();
        var receiptLines = await context.GoodsReceiptLines
            .Where(line => line.CompanyId == request.CompanyId && receiptLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var receiptLineId in receiptLineIds)
        {
            if (!receiptLines.ContainsKey(receiptLineId))
                throw new FinancePostingValidationException(
                    $"Goods-receipt line {receiptLineId} does not exist in this company; " +
                    "invoices match posted receipts only.");
        }

        // A receipt line is billable exactly once: prior invoice lines block re-matching.
        var alreadyInvoiced = await context.PurchaseInvoiceLines
            .Where(line => receiptLineIds.Contains(line.GoodsReceiptLineId))
            .Select(line => line.GoodsReceiptLineId)
            .ToListAsync(cancellationToken);
        if (alreadyInvoiced.Count > 0)
            throw new FinancePostingConflictException(
                $"Goods-receipt lines {string.Join(", ", alreadyInvoiced.OrderBy(id => id))} " +
                "are already matched by a posted invoice; credit notes correct billed receipts.");

        var invoice = new PurchaseInvoice
        {
            CompanyId = request.CompanyId,
            SupplierId = supplier.Id,
            InvoiceNumber = request.InvoiceNumber,
            InvoiceDate = request.InvoiceDate,
            DueDate = request.DueDate,
            PostingDate = postingDate,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim(),
            Lines = request.Lines
                .Select((line, index) =>
                {
                    var receiptLine = receiptLines[line.GoodsReceiptLineId];
                    return new PurchaseInvoiceLine
                    {
                        CompanyId = request.CompanyId,
                        LineNumber = index + 1,
                        GoodsReceiptLineId = receiptLine.Id,
                        ItemId = receiptLine.ItemId,
                        MatchedValue = decimal.Round(
                            receiptLine.BaseQuantity * receiptLine.UnitCost, 6, MidpointRounding.AwayFromZero)
                    };
                })
                .ToList()
        };
        invoice.TotalValue = invoice.Lines.Sum(line => line.MatchedValue);

        context.PurchaseInvoices.Add(invoice);
        // Persist first: the journal source key needs the materialized invoice identity, and the
        // invoice audit rows must carry the posting actor.
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        string? journalNumber = null;
        if (invoice.TotalValue > 0)
        {
            var grniAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.GoodsReceivedNotInvoiced, cancellationToken);
            var apAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.AccountsPayable, cancellationToken);
            var posted = await journalPosting.PostAsync(
                new JournalPostingRequest(
                    request.CompanyId,
                    postingDate,
                    company.BaseCurrency,
                    $"Supplier invoice {invoice.InvoiceNumber} from {supplier.Name}",
                    InvoiceDocumentType,
                    invoice.Id.ToString(CultureInfo.InvariantCulture),
                    "Post",
                    1,
                    [
                        new JournalLineInput(
                            grniAccount.ChartOfAccountId,
                            $"GRNI clearing {invoice.InvoiceNumber}",
                            invoice.TotalValue,
                            0m),
                        new JournalLineInput(
                            apAccount.ChartOfAccountId,
                            $"Payable {invoice.InvoiceNumber}",
                            0m,
                            invoice.TotalValue)
                    ]),
                actor,
                cancellationToken);
            journalNumber = posted.JournalNumber;
        }

        logger.LogInformation(
            "Purchase invoice {InvoiceNumber} posted for company {CompanyId}: value {TotalValue}, journal {JournalNumber}",
            invoice.InvoiceNumber, request.CompanyId, invoice.TotalValue, journalNumber ?? "none");

        return await ToResultAsync(invoice, replayed: false, cancellationToken);
    }

    private async Task<PurchaseInvoiceResult> ToResultAsync(
        PurchaseInvoice invoice,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var paid = await context.PaymentAllocations
            .Where(allocation => allocation.PurchaseInvoiceId == invoice.Id)
            .SumAsync(allocation => (decimal?)allocation.Amount, cancellationToken) ?? 0m;
        var journal = await context.JournalEntries.SingleOrDefaultAsync(
            entry => entry.SourceDocumentType == InvoiceDocumentType &&
                entry.SourceDocumentId == invoice.Id.ToString(CultureInfo.InvariantCulture) &&
                entry.SourceAction == "Post" &&
                entry.SourceVersion == 1,
            cancellationToken);
        return new PurchaseInvoiceResult(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.PostingDate,
            invoice.TotalValue,
            invoice.TotalValue - paid,
            journal?.JournalNumber,
            replayed);
    }

    private Task<PurchaseInvoice?> FindCommittedAsync(
        int companyId,
        string requestKey,
        CancellationToken cancellationToken) =>
        context.PurchaseInvoices.SingleOrDefaultAsync(
            invoice => invoice.CompanyId == companyId && invoice.RequestKey == requestKey,
            cancellationToken);

    private static void ValidateRequestShape(PostPurchaseInvoiceRequest request)
    {
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        if (request.SupplierId <= 0)
            throw new FinancePostingValidationException("A positive SupplierId is required.");
        var invoiceNumber = request.InvoiceNumber?.Trim() ?? string.Empty;
        if (invoiceNumber.Length is 0 or > 128)
            throw new FinancePostingValidationException(
                "A supplier invoice number of 1 to 128 characters is required.");
        if (request.InvoiceDate > request.DueDate)
            throw new FinancePostingValidationException("The due date must not precede the invoice date.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException(
                "A purchase-invoice request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException(
                $"A purchase invoice requires between 1 and {MaximumLines} matched lines.");
        foreach (var line in request.Lines)
        {
            if (line is null || line.GoodsReceiptLineId <= 0)
                throw new FinancePostingValidationException(
                    "Every invoice line needs a positive GoodsReceiptLineId.");
        }
    }

    private static PostPurchaseInvoiceRequest Normalize(PostPurchaseInvoiceRequest request) => request with
    {
        InvoiceNumber = request.InvoiceNumber.Trim(),
        RequestKey = request.RequestKey.Trim(),
        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
    };

    private static string ComputeRequestHash(PostPurchaseInvoiceRequest request)
    {
        var canonical = new StringBuilder();
        void Append(string value) =>
            canonical.Append(value.Replace("\u001f", " ", StringComparison.Ordinal)).Append('\u001f');

        Append(request.CompanyId.ToString(CultureInfo.InvariantCulture));
        Append(request.SupplierId.ToString(CultureInfo.InvariantCulture));
        Append(request.InvoiceNumber);
        Append(request.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(request.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Append(request.RequestKey);
        Append((request.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty);
        Append(request.Notes ?? string.Empty);
        foreach (var line in request.Lines)
        {
            Append(line.GoodsReceiptLineId.ToString(CultureInfo.InvariantCulture));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
