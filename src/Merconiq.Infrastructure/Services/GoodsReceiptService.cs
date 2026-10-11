using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Posts goods receipts against approved purchase orders. One committed receipt atomically
/// produces: immutable receipt evidence, valued stock intake through the existing stock ledger,
/// and one balanced journal (Dr Inventory / Cr Goods-Received-Not-Invoiced) through the finance
/// kernel. Purchase-order approval alone never posts; only this service receipts stock.
/// </summary>
public sealed class GoodsReceiptService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IStockService stockService,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    IDocumentNumberService documentNumbers,
    ILogger<GoodsReceiptService> logger) : IGoodsReceiptService
{
    /// <summary>Unique idempotency index; duplicate submissions classify against this name.</summary>
    public const string RequestKeyIndex = "UX_GoodsReceipts_RequestKey";

    public const string ReceiptDocumentType = "GoodsReceipt";
    public const string ReceiptNumberPrefix = "GR";

    private const int MaximumLines = 200;

    /// <inheritdoc />
    public async Task<GoodsReceiptResult> PostGoodsReceiptAsync(
        PostGoodsReceiptRequest request,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ValidateRequestShape(request);
        var normalized = Normalize(request);
        var requestHash = ComputeRequestHash(normalized);

        GoodsReceiptResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"goods-receipt:{normalized.CompanyId}", cancellationToken);

                var committed = await FindCommittedAsync(normalized.CompanyId, normalized.RequestKey, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This goods-receipt request key was already posted with a different payload.");
                    result = await ToResultAsync(committed, replayed: true, cancellationToken);
                    return;
                }

                result = await PostCoreAsync(normalized, requestHash, mutationScope, actor, cancellationToken);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () =>
                await FindCommittedAsync(normalized.CompanyId, normalized.RequestKey, cancellationToken) is not null);

        return result;
    }

    /// <inheritdoc />
    public Task<GoodsReceipt?> GetGoodsReceiptAsync(
        int companyId,
        int goodsReceiptId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.GoodsReceipts
            .Include(receipt => receipt.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(receipt => receipt.Id == goodsReceiptId && receipt.CompanyId == companyId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoodsReceipt>> ListGoodsReceiptsAsync(
        int companyId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await context.GoodsReceipts
            .Where(receipt => receipt.CompanyId == companyId)
            .OrderByDescending(receipt => receipt.ReceivedAtUtc)
            .ThenByDescending(receipt => receipt.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<GoodsReceiptResult> PostCoreAsync(
        PostGoodsReceiptRequest request,
        string requestHash,
        StockMutationScope mutationScope,
        string actor,
        CancellationToken cancellationToken)
    {
        var company = await context.Companies.SingleOrDefaultAsync(
                candidate => candidate.Id == request.CompanyId && candidate.IsActive, cancellationToken)
            ?? throw new FinancePostingValidationException("Active company not found.");

        // Company ownership of a purchase order is carried by its document identity.
        var order = await context.PurchaseOrders
            .Include(po => po.OrderDetails)
            .Include(po => po.DocumentIdentity)
            .Include(po => po.Supplier)
            .SingleOrDefaultAsync(
                po => po.Id == request.PurchaseOrderId &&
                    po.DocumentIdentity.CompanyId == request.CompanyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Purchase order not found in this company.");
        if (order.Status != PurchaseOrderStatus.Approved)
            throw new FinancePostingValidationException(
                $"Only approved purchase orders can receive goods; this order is {order.Status}.");

        var postingDate = request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        await fiscalCalendar.EnsurePeriodOpenForDateAsync(request.CompanyId, postingDate, cancellationToken);

        var poLineIds = request.Lines.Select(line => line.PurchaseOrderLineId).Distinct().ToList();
        var orderLines = order.OrderDetails
            .Where(line => line.Direction == DocumentLineDirection.Charge && line.Quantity > 0)
            .ToDictionary(line => line.Id);
        foreach (var poLineId in poLineIds)
        {
            if (!orderLines.ContainsKey(poLineId))
                throw new FinancePostingValidationException(
                    $"Purchase-order line {poLineId} does not belong to order {order.PONumber}.");
        }

        // Receipt progress is derived from posted receipt lines, never from mutable counters.
        var priorReceipts = await context.GoodsReceiptLines
            .Where(line => poLineIds.Contains(line.PurchaseOrderLineId))
            .GroupBy(line => line.PurchaseOrderLineId)
            .Select(group => new
            {
                PurchaseOrderLineId = group.Key,
                Accepted = group.Sum(line => line.AcceptedQuantity),
                Rejected = group.Sum(line => line.RejectedQuantity)
            })
            .ToDictionaryAsync(
                row => row.PurchaseOrderLineId,
                row => (Accepted: row.Accepted, Rejected: row.Rejected),
                cancellationToken);

        var items = await context.Items
            .Where(item => orderLines.Values.Select(line => line.ItemId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var builtLines = new List<GoodsReceiptLine>();
        var inRequestTaken = new Dictionary<int, int>();
        foreach (var line in request.Lines)
        {
            var orderLine = orderLines[line.PurchaseOrderLineId];
            var accepted = line.AcceptedQuantity;
            var rejected = line.RejectedQuantity;
            if (accepted + rejected <= 0)
                throw new FinancePostingValidationException(
                    "Every receipt line must accept or reject a positive quantity.");
            if (!items.TryGetValue(orderLine.ItemId, out var item))
                throw new FinancePostingValidationException("The ordered item no longer exists.");

            priorReceipts.TryGetValue(line.PurchaseOrderLineId, out var prior);
            var priorTaken = prior.Accepted + prior.Rejected +
                inRequestTaken.GetValueOrDefault(line.PurchaseOrderLineId);
            if (priorTaken + accepted + rejected > orderLine.Quantity)
                throw new FinancePostingValidationException(
                    $"Receipting {accepted + rejected} more of purchase-order line {line.PurchaseOrderLineId} " +
                    $"would exceed the ordered {orderLine.Quantity} (already taken {priorTaken}). " +
                    "Over-receipt tolerance is not enabled in this slice.");
            inRequestTaken[line.PurchaseOrderLineId] =
                inRequestTaken.GetValueOrDefault(line.PurchaseOrderLineId) + accepted + rejected;

            // v1 constraint (see docs/FINANCE_POSTING.md and finding F-03): stock is whole-unit
            // base quantity, so the purchase factor must convert this receipt to whole units.
            var factor = item.PurchaseToBaseFactor;
            if (factor <= 0)
                throw new FinancePostingValidationException(
                    $"Item '{item.ItemCode}' has an invalid purchase-to-base factor.");
            var baseQuantityDecimal = accepted * factor;
            if (baseQuantityDecimal != decimal.Truncate(baseQuantityDecimal))
                throw new FinancePostingValidationException(
                    $"Item '{item.ItemCode}' purchase factor {factor} converts the accepted quantity " +
                    "to fractional base units; whole-unit stock does not support this yet.");

            // Per-base-unit cost derives from the approved commercial price; stock valuation and
            // the GRNI journal book exactly this cost.
            var unitCost = Round6(orderLine.UnitPrice / factor);

            builtLines.Add(new GoodsReceiptLine
            {
                CompanyId = request.CompanyId,
                LineNumber = builtLines.Count + 1,
                PurchaseOrderLineId = orderLine.Id,
                ItemId = orderLine.ItemId,
                AcceptedQuantity = accepted,
                RejectedQuantity = rejected,
                BaseQuantity = (int)baseQuantityDecimal,
                UnitCost = unitCost,
                BatchNumber = string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim(),
                ExpiryDate = StockLotExpiryDate.Normalize(line.ExpiryDate)
            });
        }

        var receiptNumber = await documentNumbers.AllocateAsync(
            request.CompanyId, ReceiptDocumentType, postingDate.Year, ReceiptNumberPrefix, cancellationToken);

        var receipt = new GoodsReceipt
        {
            CompanyId = request.CompanyId,
            PurchaseOrderId = order.Id,
            LocationId = request.LocationId,
            ReceiptNumber = receiptNumber,
            PostingDate = postingDate,
            SupplierDeliveryReference = request.SupplierDeliveryReference,
            Notes = request.Notes,
            RequestKey = request.RequestKey,
            RequestHash = requestHash,
            ReceivedAtUtc = DateTime.UtcNow,
            ReceivedBy = actor.Trim(),
            Lines = builtLines
        };
        context.GoodsReceipts.Add(receipt);
        // Persist the receipt first so it carries the posting actor in its audit rows and its
        // identity is available as the journal source; stock intake and the journal follow in
        // the same transaction.
        await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

        foreach (var line in builtLines.Where(line => line.BaseQuantity > 0))
        {
            // Joins the caller's transaction; the stock service revalidates location company
            // scope under its location lock and books the moving-average valuation.
            await stockService.ReceiveStockAsync(
                line.ItemId,
                request.LocationId,
                line.BaseQuantity,
                notes: $"Goods receipt {receiptNumber} against {order.PONumber}",
                batchNumber: line.BatchNumber,
                expiryDate: line.ExpiryDate,
                unitCost: line.UnitCost,
                mutationScope: mutationScope,
                cancellationToken: cancellationToken);
        }

        string? journalNumber = null;
        var acceptedValue = builtLines.Sum(line => Round6(line.BaseQuantity * line.UnitCost));
        if (acceptedValue > 0)
        {
            var inventoryAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.Inventory, cancellationToken);
            var grniAccount = await postingPolicy.ResolveAsync(
                request.CompanyId, AccountPostingRole.GoodsReceivedNotInvoiced, cancellationToken);
            var posted = await journalPosting.PostAsync(
                new JournalPostingRequest(
                    request.CompanyId,
                    postingDate,
                    company.BaseCurrency,
                    $"Goods receipt {receiptNumber} against {order.PONumber}",
                    ReceiptDocumentType,
                    receipt.Id.ToString(CultureInfo.InvariantCulture),
                    "Post",
                    1,
                    [
                        new JournalLineInput(
                            inventoryAccount.ChartOfAccountId,
                            $"Stock intake {receiptNumber}",
                            acceptedValue,
                            0m),
                        new JournalLineInput(
                            grniAccount.ChartOfAccountId,
                            $"GRNI {receiptNumber}",
                            0m,
                            acceptedValue)
                    ]),
                actor,
                cancellationToken);
            journalNumber = posted.JournalNumber;
        }

        logger.LogInformation(
            "Goods receipt {ReceiptNumber} {Outcome} for company {CompanyId} against PO {PONumber}: accepted value {AcceptedValue}, journal {JournalNumber}",
            receiptNumber,
            "posted",
            request.CompanyId,
            order.PONumber,
            acceptedValue,
            journalNumber ?? "none");

        return new GoodsReceiptResult(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.PostingDate,
            builtLines.Sum(line => line.AcceptedQuantity),
            acceptedValue,
            journalNumber,
            Replayed: false);
    }

    private async Task<GoodsReceiptResult> ToResultAsync(
        GoodsReceipt receipt,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var journal = await context.JournalEntries.SingleOrDefaultAsync(
            entry => entry.SourceDocumentType == ReceiptDocumentType &&
                entry.SourceDocumentId == receipt.Id.ToString(CultureInfo.InvariantCulture) &&
                entry.SourceAction == "Post" &&
                entry.SourceVersion == 1,
            cancellationToken);
        return new GoodsReceiptResult(
            receipt.Id,
            receipt.ReceiptNumber,
            receipt.PostingDate,
            receipt.Lines.Sum(line => line.AcceptedQuantity),
            receipt.Lines.Sum(line => Round6(line.BaseQuantity * line.UnitCost)),
            journal?.JournalNumber,
            replayed);
    }

    private Task<GoodsReceipt?> FindCommittedAsync(
        int companyId,
        string requestKey,
        CancellationToken cancellationToken) =>
        context.GoodsReceipts.SingleOrDefaultAsync(
            receipt => receipt.CompanyId == companyId && receipt.RequestKey == requestKey,
            cancellationToken);

    private static void ValidateRequestShape(PostGoodsReceiptRequest request)
    {
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        if (request.PurchaseOrderId <= 0)
            throw new FinancePostingValidationException("A positive PurchaseOrderId is required.");
        if (request.LocationId <= 0)
            throw new FinancePostingValidationException("A positive LocationId is required.");
        var requestKey = request.RequestKey?.Trim() ?? string.Empty;
        if (requestKey.Length is 0 or > 200)
            throw new FinancePostingValidationException(
                "A goods-receipt request key of 1 to 200 characters is required.");
        if (request.Lines is null || request.Lines.Count is 0 or > MaximumLines)
            throw new FinancePostingValidationException(
                $"A goods receipt requires between 1 and {MaximumLines} lines.");
        foreach (var line in request.Lines)
        {
            if (line is null)
                throw new FinancePostingValidationException("Receipt lines must not be null.");
            if (line.PurchaseOrderLineId <= 0)
                throw new FinancePostingValidationException(
                    "Every receipt line needs a positive PurchaseOrderLineId.");
            if (line.AcceptedQuantity < 0 || line.RejectedQuantity < 0)
                throw new FinancePostingValidationException("Receipt quantities must not be negative.");
        }
    }

    private static PostGoodsReceiptRequest Normalize(PostGoodsReceiptRequest request) => request with
    {
        RequestKey = request.RequestKey.Trim(),
        SupplierDeliveryReference = string.IsNullOrWhiteSpace(request.SupplierDeliveryReference)
            ? null
            : request.SupplierDeliveryReference.Trim(),
        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        Lines = request.Lines
            .Select(line => line with
            {
                BatchNumber = string.IsNullOrWhiteSpace(line.BatchNumber) ? null : line.BatchNumber.Trim()
            })
            .ToArray()
    };

    private static string ComputeRequestHash(PostGoodsReceiptRequest request)
    {
        var canonical = new StringBuilder();
        void Append(string value) =>
            canonical.Append(value.Replace("\u001f", " ", StringComparison.Ordinal)).Append('\u001f');

        Append(request.CompanyId.ToString(CultureInfo.InvariantCulture));
        Append(request.PurchaseOrderId.ToString(CultureInfo.InvariantCulture));
        Append(request.LocationId.ToString(CultureInfo.InvariantCulture));
        Append((request.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) ?? string.Empty);
        Append(request.RequestKey);
        Append(request.SupplierDeliveryReference ?? string.Empty);
        Append(request.Notes ?? string.Empty);
        foreach (var line in request.Lines)
        {
            Append(line.PurchaseOrderLineId.ToString(CultureInfo.InvariantCulture));
            Append(line.AcceptedQuantity.ToString(CultureInfo.InvariantCulture));
            Append(line.RejectedQuantity.ToString(CultureInfo.InvariantCulture));
            Append(line.BatchNumber ?? string.Empty);
            Append(line.ExpiryDate?.ToString("O") ?? string.Empty);
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static decimal Round6(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
