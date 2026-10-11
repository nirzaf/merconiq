namespace Merconiq.Core.Entities;

/// <summary>One credited line against a posted goods-receipt line; quantity bounded per line.</summary>
public sealed class PurchaseCreditNoteLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PurchaseCreditNoteId { get; set; }

    public int LineNumber { get; set; }

    public int GoodsReceiptLineId { get; set; }

    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Returned quantity in the receipt line's unit.</summary>
    public int Quantity { get; set; }

    /// <summary>Credited value: quantity times the receipt line's snapshotted unit cost.</summary>
    public decimal CreditValue { get; set; }

    public PurchaseCreditNote PurchaseCreditNote { get; set; } = null!;
}
