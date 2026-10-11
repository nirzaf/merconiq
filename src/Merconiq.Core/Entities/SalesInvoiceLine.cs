namespace Merconiq.Core.Entities;

/// <summary>
/// One invoiced line matched to exactly one posted delivery line. Revenue value is the
/// delivered base quantity times the approved order-line selling price, snapshotted here.
/// </summary>
public sealed class SalesInvoiceLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SalesInvoiceId { get; set; }

    public int LineNumber { get; set; }

    /// <summary>The posted delivery line this invoice line matches; consumed exactly once.</summary>
    public int DeliveryNoteLineId { get; set; }

    public DeliveryNoteLine DeliveryNoteLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Invoiced quantity in base units; equals the delivered quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Revenue value: quantity times the approved selling price.</summary>
    public decimal RevenueValue { get; set; }

    public SalesInvoice SalesInvoice { get; set; } = null!;
}
