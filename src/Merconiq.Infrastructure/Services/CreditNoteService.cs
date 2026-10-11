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
/// Posts bounded returns and credit notes against posted evidence. A supplier credit note issues
/// stock back to the supplier at moving-average value and posts Dr AP / Cr Inventory. A sales
/// credit note returns stock through the ledger and posts Dr Sales / Cr AR plus the restock
/// reversal Dr Inventory / Cr COGS. Both are idempotent per request key, immutable, and bounded
/// per line so no more than the received/delivered quantity can ever be credited.
/// </summary>
public sealed class CreditNoteService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IStockService stockService,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    ILogger<CreditNoteService> logger) : ICreditNoteService
{
    public const string PurchaseRequestKeyIndex = "UX_PurchaseCreditNotes_RequestKey";
    public const string SalesRequestKeyIndex = "UX_SalesCreditNotes_RequestKey";

    private const int MaximumLines = 200;

    /// <inheritdoc />
    public async Task<PurchaseCreditNote> PostPurchaseCreditNoteAsync(
        PostPurchaseCreditNoteRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateShape(request.CompanyId, request.SupplierId, request.CreditNoteNumber,
            request.Reason, request.RequestKey, request.Lines.Count,
            request.Lines?.Any(line => line is null || line.GoodsReceiptLineId <= 0 || line.Quantity <= 0) != false);
        var normalized = request with
        {
            CreditNoteNumber = request.CreditNoteNumber.Trim(),
            Reason = request.Reason.Trim(),
            RequestKey = request.RequestKey.Trim()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("purchase-credit\u001f").Append(request.CompanyId).Append('\u001f')
                .Append(request.SupplierId).Append('\u001f').Append(normalized.CreditNoteNumber)
                .Append('\u001f').Append(normalized.RequestKey).Append('\u001f').Append(normalized.Reason)
                .Append('\u001f');
            foreach (var line in normalized.Lines)
            {
                h.Append(line.GoodsReceiptLineId).Append('\u001f').Append(line.Quantity).Append('\u001f');
            }
        });

        PurchaseCreditNote result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"purchase-credit:{normalized.CompanyId}", cancellationToken);
                var committed = await context.PurchaseCreditNotes
                    .Include(note => note.Lines)
                    .SingleOrDefaultAsync(note => note.CompanyId == normalized.CompanyId &&
                        note.RequestKey == normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This credit-note request key was already posted with a different payload.");
                    result = committed;
                    return;
                }

                result = await PurchaseCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.PurchaseCreditNotes.AnyAsync(
                note => note.CompanyId == normalized.CompanyId && note.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<SalesCreditNote> PostSalesCreditNoteAsync(
        PostSalesCreditNoteRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateShape(request.CompanyId, request.CustomerId, request.CreditNoteNumber,
            request.Reason, request.RequestKey, request.Lines.Count,
            request.Lines?.Any(line => line is null || line.DeliveryNoteLineId <= 0 || line.Quantity <= 0) != false);
        var normalized = request with
        {
            CreditNoteNumber = request.CreditNoteNumber.Trim(),
            Reason = request.Reason.Trim(),
            RequestKey = request.RequestKey.Trim()
        };
        var requestHash = ComputeHash(h =>
        {
            h.Append("sales-credit\u001f").Append(request.CompanyId).Append('\u001f')
                .Append(request.CustomerId).Append('\u001f').Append(normalized.CreditNoteNumber)
                .Append('\u001f').Append(normalized.RequestKey).Append('\u001f').Append(normalized.Reason)
                .Append('\u001f');
            foreach (var line in normalized.Lines)
            {
                h.Append(line.DeliveryNoteLineId).Append('\u001f').Append(line.Quantity).Append('\u001f')
                    .Append((int)line.Disposition).Append('\u001f');
            }
        });

        SalesCreditNote result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"sales-credit:{normalized.CompanyId}", cancellationToken);
                var committed = await context.SalesCreditNotes
                    .Include(note => note.Lines)
                    .SingleOrDefaultAsync(note => note.CompanyId == normalized.CompanyId &&
                        note.RequestKey == normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This credit-note request key was already posted with a different payload.");
                    result = committed;
                    return;
                }

                result = await SalesCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.SalesCreditNotes.AnyAsync(
                note => note.CompanyId == normalized.CompanyId && note.RequestKey == normalized.RequestKey,
                cancellationToken));

        return result;
    }

    private async Task<PurchaseCreditNote> PurchaseCoreAsync(
        PostPurchaseCreditNoteRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var supplier = await context.Suppliers.SingleOrDefaultAsync(
                candidate => candidate.Id == request.SupplierId, cancellationToken)
            ?? throw new FinancePostingValidationException("Supplier not found.");

        var duplicate = await context.PurchaseCreditNotes.AnyAsync(
            note => note.CompanyId == request.CompanyId &&
                note.SupplierId == request.SupplierId &&
                note.CreditNoteNumber == request.CreditNoteNumber,
            cancellationToken);
        if (duplicate)
            throw new FinancePostingConflictException(
                $"A credit note numbered '{request.CreditNoteNumber}' already exists for this supplier in this company.");

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
                    "credit notes match posted receipts only.");
        }

        // A receipt line already billed by an invoice is corrected by an invoice credit note,
        // not by a stock return in this slice.
        var invoiced = await context.PurchaseInvoiceLines
            .Where(line => receiptLineIds.Contains(line.GoodsReceiptLineId))
            .Select(line => line.GoodsReceiptLineId)
            .ToListAsync(cancellationToken);
        if (invoiced.Count > 0)
            throw new FinancePostingConflictException(
                $"Goods-receipt lines {string.Join(", ", invoiced.OrderBy(id => id))} are already matched " +
                "by a posted invoice; supplier invoice credit notes are a future increment.");

        // Bounded by accepted minus already-returned, including this request's own lines.
        var priorReturned = await context.PurchaseCreditNoteLines
            .Where(line => receiptLineIds.Contains(line.GoodsReceiptLineId))
            .GroupBy(line => line.GoodsReceiptLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.LineId, row => row.Quantity, cancellationToken);

        var note = new PurchaseCreditNote
        {
            CompanyId = request.CompanyId,
            SupplierId = supplier.Id,
            CreditNoteNumber = request.CreditNoteNumber,
            PostingDate = postingDate,
            Reason = request.Reason,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim()
        };
        var noteTotal = 0m;
        var inRequestReturned = new Dictionary<int, int>();
        foreach (var line in request.Lines)
        {
            var receiptLine = receiptLines[line.GoodsReceiptLineId];
            priorReturned.TryGetValue(line.GoodsReceiptLineId, out var prior);
            var taken = prior + inRequestReturned.GetValueOrDefault(line.GoodsReceiptLineId);
            if (taken + line.Quantity > receiptLine.AcceptedQuantity)
                throw new FinancePostingValidationException(
                    $"Crediting {line.Quantity} of receipt line {line.GoodsReceiptLineId} exceeds the " +
                    $"returnable balance of {receiptLine.AcceptedQuantity - taken}.");

            // Convert the PO-unit return quantity to base units; whole-unit stock requires the
            // conversion to be exact rather than silently truncated.
            var baseQuantity = receiptLine.BaseQuantity == 0
                ? line.Quantity
                : (decimal)line.Quantity * receiptLine.BaseQuantity / receiptLine.AcceptedQuantity;
            if (baseQuantity != decimal.Truncate(baseQuantity))
                throw new FinancePostingValidationException(
                    "The return quantity converts to fractional base units for this receipt line; " +
                    "whole-unit stock does not support this yet.");
            var baseToReturn = (int)baseQuantity;

            // Issue the returned stock out through the ledger at moving average; the issued
            // valuation entry is the exact credited stock value.
            var knownTransactionIds = context.StockTransactions.Local.Select(t => t.Id).ToHashSet();
            var receiptLocationId = await context.GoodsReceipts
                .Where(r => r.Id == receiptLine.GoodsReceiptId)
                .Select(r => r.LocationId)
                .SingleAsync(cancellationToken);
            await stockService.SellStockAsync(
                receiptLine.ItemId,
                receiptLocationId,
                baseToReturn,
                notes: $"Supplier credit note: {request.Reason}",
                batchNumber: receiptLine.BatchNumber,
                expiryDate: receiptLine.ExpiryDate,
                mutationScope: mutationScope);
            var issued = context.StockTransactions.Local
                .Where(t => !knownTransactionIds.Contains(t.Id) &&
                    t.ItemId == receiptLine.ItemId &&
                    t.Quantity == baseToReturn)
                .OrderByDescending(t => t.Id)
                .FirstOrDefault()
                ?? throw new FinancePostingValidationException(
                    "The stock ledger did not record the returned movement; credit note aborted.");

            var issuedValue = (await context.StockValuationEntries
                .Where(entry => entry.StockTransactionId == issued.Id)
                .Select(entry => (decimal?)entry.TotalValue)
                .FirstOrDefaultAsync(cancellationToken)) ?? 0m;

            note.Lines.Add(new PurchaseCreditNoteLine
            {
                CompanyId = request.CompanyId,
                LineNumber = note.Lines.Count + 1,
                GoodsReceiptLineId = receiptLine.Id,
                ItemId = receiptLine.ItemId,
                Quantity = line.Quantity,
                CreditValue = issuedValue
            });
            noteTotal += issuedValue;
            inRequestReturned[line.GoodsReceiptLineId] =
                inRequestReturned.GetValueOrDefault(line.GoodsReceiptLineId) + line.Quantity;
        }

        note.TotalValue = noteTotal;
        context.PurchaseCreditNotes.Add(note);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        if (noteTotal > 0)
        {
            var apAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.AccountsPayable, cancellationToken);
            var inventoryAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.Inventory, cancellationToken);
            await journalPosting.PostAsync(
                new JournalPostingRequest(
                    request.CompanyId,
                    postingDate,
                    company.BaseCurrency,
                    $"Supplier credit note {note.CreditNoteNumber}: {request.Reason}",
                    "PurchaseCreditNote",
                    note.Id.ToString(CultureInfo.InvariantCulture),
                    "Post",
                    1,
                    [
                        new JournalLineInput(apAccount.ChartOfAccountId, $"Payable credit {note.CreditNoteNumber}", noteTotal, 0m),
                        new JournalLineInput(inventoryAccount.ChartOfAccountId, "Stock returned to supplier", 0m, noteTotal)
                    ]),
                actor,
                cancellationToken);
        }

        logger.LogInformation(
            "Purchase credit note {Number} posted for company {CompanyId}: value {Total}",
            note.CreditNoteNumber, request.CompanyId, noteTotal);
        return note;
    }

    private async Task<SalesCreditNote> SalesCoreAsync(
        PostSalesCreditNoteRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await RequireCompanyAsync(request.CompanyId, cancellationToken);
        var customer = await context.Customers.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CustomerId && candidate.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Customer not found in this company.");

        var duplicate = await context.SalesCreditNotes.AnyAsync(
            note => note.CompanyId == request.CompanyId && note.CreditNoteNumber == request.CreditNoteNumber,
            cancellationToken);
        if (duplicate)
            throw new FinancePostingConflictException(
                $"A sales credit note numbered '{request.CreditNoteNumber}' already exists in this company.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var deliveryLineIds = request.Lines.Select(line => line.DeliveryNoteLineId).Distinct().ToList();
        var deliveryLines = await context.DeliveryNoteLines
            .Where(line => line.CompanyId == request.CompanyId && deliveryLineIds.Contains(line.Id))
            .Include(line => line.SalesOrderLine)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var deliveryLineId in deliveryLineIds)
        {
            if (!deliveryLines.ContainsKey(deliveryLineId))
                throw new FinancePostingValidationException(
                    $"Delivery line {deliveryLineId} does not exist in this company; " +
                    "credit notes match posted deliveries only.");
        }

        var priorReturned = await context.SalesCreditNoteLines
            .Where(line => deliveryLineIds.Contains(line.DeliveryNoteLineId))
            .GroupBy(line => line.DeliveryNoteLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(row => row.LineId, row => row.Quantity, cancellationToken);

        var note = new SalesCreditNote
        {
            CompanyId = request.CompanyId,
            CustomerId = customer.Id,
            CreditNoteNumber = request.CreditNoteNumber,
            PostingDate = postingDate,
            Reason = request.Reason,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim()
        };
        var creditTotal = 0m;
        foreach (var line in request.Lines)
        {
            var deliveryLine = deliveryLines[line.DeliveryNoteLineId];
            priorReturned.TryGetValue(line.DeliveryNoteLineId, out var prior);
            if (prior + line.Quantity > deliveryLine.Quantity)
                throw new FinancePostingValidationException(
                    $"Crediting {line.Quantity} of delivery line {line.DeliveryNoteLineId} exceeds the " +
                    $"returnable balance of {deliveryLine.Quantity - prior}.");

            int? returnTransactionId = null;
            if (line.Disposition == StockReturnDisposition.Restockable)
            {
                var knownValuationIds = context.StockValuationEntries.Local.Select(entry => entry.Id).ToHashSet();
                await stockService.ReturnStockAsync(
                    new CreateStockReturnRequest(
                        deliveryLine.StockTransactionId,
                        line.Quantity,
                        line.Disposition,
                        $"SalesCreditNote:{request.RequestKey}:{line.DeliveryNoteLineId}",
                        Notes: $"Sales credit note: {request.Reason}"),
                    mutationScope,
                    cancellationToken);
                var returnEntry = context.StockValuationEntries.Local
                    .Where(entry => !knownValuationIds.Contains(entry.Id))
                    .OrderByDescending(entry => entry.Id)
                    .FirstOrDefault();
                returnTransactionId = returnEntry?.StockTransactionId;
            }

            var creditValue = decimal.Round(
                line.Quantity * deliveryLine.SalesOrderLine.UnitPrice, 6, MidpointRounding.AwayFromZero);
            note.Lines.Add(new SalesCreditNoteLine
            {
                CompanyId = request.CompanyId,
                LineNumber = note.Lines.Count + 1,
                DeliveryNoteLineId = deliveryLine.Id,
                ItemId = deliveryLine.ItemId,
                Quantity = line.Quantity,
                StockTransactionId = returnTransactionId,
                Disposition = line.Disposition,
                CreditValue = creditValue
            });
            creditTotal += creditValue;
        }

        note.TotalValue = creditTotal;
        context.SalesCreditNotes.Add(note);
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        var arAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.AccountsReceivable, cancellationToken);
        var revenueAccount = await postingPolicy.ResolveAsync(
            request.CompanyId, AccountPostingRole.SalesRevenue, cancellationToken);
        await journalPosting.PostAsync(
            new JournalPostingRequest(
                request.CompanyId,
                postingDate,
                company.BaseCurrency,
                $"Sales credit note {note.CreditNoteNumber}: {request.Reason}",
                "SalesCreditNote",
                note.Id.ToString(CultureInfo.InvariantCulture),
                "Post",
                1,
                [
                    new JournalLineInput(revenueAccount.ChartOfAccountId, $"Revenue reversal {note.CreditNoteNumber}", creditTotal, 0m),
                    new JournalLineInput(arAccount.ChartOfAccountId, $"Receivable credit {note.CreditNoteNumber}", 0m, creditTotal)
                ]),
            actor,
            cancellationToken);

        var restocked = 0m;
        var restockIds = note.Lines
            .Where(line => line.StockTransactionId is not null)
            .Select(line => line.StockTransactionId!.Value)
            .ToList();
        if (restockIds.Count > 0)
        {
            var values = await context.StockValuationEntries
                .Where(entry => restockIds.Contains(entry.StockTransactionId))
                .ToDictionaryAsync(entry => entry.StockTransactionId, entry => entry.TotalValue, cancellationToken);
            restocked = restockIds.Sum(id => values.GetValueOrDefault(id));
            if (restocked > 0)
            {
                var inventoryAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.Inventory, cancellationToken);
                var cogsAccount = await postingPolicy.ResolveAsync(
                    request.CompanyId, AccountPostingRole.CostOfGoodsSold, cancellationToken);
                await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        request.CompanyId,
                        postingDate,
                        company.BaseCurrency,
                        $"Stock restocked for sales credit note {note.CreditNoteNumber}",
                        "SalesCreditNote",
                        $"{note.Id}-restock",
                        "Post",
                        1,
                        [
                            new JournalLineInput(inventoryAccount.ChartOfAccountId, "Stock returned", restocked, 0m),
                            new JournalLineInput(cogsAccount.ChartOfAccountId, "COGS reversal", 0m, restocked)
                        ]),
                    actor,
                    cancellationToken);
            }
        }

        logger.LogInformation(
            "Sales credit note {Number} posted for company {CompanyId}: revenue credit {Total}, restocked {Restocked}",
            note.CreditNoteNumber, request.CompanyId, creditTotal, restocked);
        return note;
    }

    private Task<Company> RequireCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        context.Companies.SingleOrDefaultAsync(candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private static void ValidateShape(
        int companyId,
        int partyId,
        string? number,
        string? reason,
        string? requestKey,
        int lineCount,
        bool linesInvalid)
    {
        if (companyId <= 0 || partyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId and party id are required.");
        var noteNumber = number?.Trim() ?? string.Empty;
        if (noteNumber.Length is 0 or > 128)
            throw new FinancePostingValidationException("A credit-note number of 1 to 128 characters is required.");
        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length is 0 or > 500)
            throw new FinancePostingValidationException("A credit-note reason of 1 to 500 characters is required.");
        var key = requestKey?.Trim() ?? string.Empty;
        if (key.Length is 0 or > 200)
            throw new FinancePostingValidationException("A credit-note request key of 1 to 200 characters is required.");
        if (lineCount is 0 or > MaximumLines || linesInvalid)
            throw new FinancePostingValidationException(
                $"A credit note requires between 1 and {MaximumLines} valid lines.");
    }

    private static string ComputeHash(Action<StringBuilder> build)
    {
        var canonical = new StringBuilder();
        build(canonical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
