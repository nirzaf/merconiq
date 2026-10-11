namespace Merconiq.Core.Models;

/// <summary>One requested receipt line against a purchase-order line, in base stock units.</summary>
public sealed record PostGoodsReceiptLine(
    int PurchaseOrderLineId,
    int AcceptedQuantity,
    int RejectedQuantity = 0,
    string? BatchNumber = null,
    DateTime? ExpiryDate = null);

/// <summary>
/// Posts one goods receipt against an approved purchase order. The request key makes the whole
/// receipt idempotent: committed requests replay, conflicting payloads are rejected.
/// </summary>
public sealed record PostGoodsReceiptRequest(
    int CompanyId,
    int PurchaseOrderId,
    int LocationId,
    string RequestKey,
    IReadOnlyList<PostGoodsReceiptLine> Lines,
    DateOnly? PostingDate = null,
    string? SupplierDeliveryReference = null,
    string? Notes = null);

/// <summary>The committed-or-replayed receipt outcome.</summary>
public sealed record GoodsReceiptResult(
    int GoodsReceiptId,
    string ReceiptNumber,
    DateOnly PostingDate,
    int AcceptedQuantity,
    decimal AcceptedValue,
    string? JournalNumber,
    bool Replayed);

/// <summary>Accepted and rejected evidence recorded for one received purchase-order line.</summary>
public sealed record GoodsReceiptLineResult(
    int LineId,
    int PurchaseOrderLineId,
    int ItemId,
    int AcceptedQuantity,
    int RejectedQuantity,
    decimal UnitCost);
