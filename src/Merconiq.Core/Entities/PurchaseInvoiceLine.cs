namespace Merconiq.Core.Entities;

/// <summary>
/// One immutable invoice line matched to exactly one posted goods-receipt line. The matched
/// value is the receipt line's accrued value (base quantity times its snapshotted unit cost), so
/// GRNI clearing and the payable equal exactly what receiving booked.
/// </summary>
public sealed class PurchaseInvoiceLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PurchaseInvoiceId { get; set; }

    /// <summary>One-based line ordinal inside the invoice.</summary>
    public int LineNumber { get; set; }

    /// <summary>The posted goods-receipt line this invoice line matches; consumed exactly once.</summary>
    public int GoodsReceiptLineId { get; set; }

    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Matched value in base currency; equals the receipt line's accrued value.</summary>
    public decimal MatchedValue { get; set; }

    public PurchaseInvoice PurchaseInvoice { get; set; } = null!;
}
