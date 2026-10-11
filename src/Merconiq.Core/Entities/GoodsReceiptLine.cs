namespace Merconiq.Core.Entities;

/// <summary>
/// One immutable goods-receipt line tied to exactly one purchase-order line. Quantities are
/// recorded in the purchase-order line's unit; <see cref="BaseQuantity"/> is the accepted
/// quantity converted to base stock units via the item's purchase factor (whole units only).
/// The unit cost is the per-base-unit acquisition cost booked to stock valuation and to the
/// Inventory/GRNI journal, snapshotted here so later master-data or price edits never rewrite
/// receipt history.
/// </summary>
public sealed class GoodsReceiptLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int GoodsReceiptId { get; set; }

    /// <summary>One-based line ordinal inside the receipt.</summary>
    public int LineNumber { get; set; }

    public int PurchaseOrderLineId { get; set; }

    public OrderDetail PurchaseOrderLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Quantity accepted against the purchase-order line, in that line's unit.</summary>
    public int AcceptedQuantity { get; set; }

    /// <summary>Quantity rejected at the dock, in the purchase-order line's unit; recorded only, never stocked.</summary>
    public int RejectedQuantity { get; set; }

    /// <summary>Accepted quantity converted to base stock units; stocked and valued quantity.</summary>
    public int BaseQuantity { get; set; }

    /// <summary>Per-base-unit acquisition cost used for valuation and the GRNI journal.</summary>
    public decimal UnitCost { get; set; }

    public string? BatchNumber { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public GoodsReceipt GoodsReceipt { get; set; } = null!;
}
