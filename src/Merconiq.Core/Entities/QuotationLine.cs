namespace Merconiq.Core.Entities;

/// <summary>One quoted line; price and value snapshotted at creation.</summary>
public sealed class QuotationLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int QuotationId { get; set; }

    public int LineNumber { get; set; }

    public int ItemId { get; set; }

    /// <summary>Quoted quantity in base stock units.</summary>
    public int Quantity { get; set; }

    /// <summary>Quoted per-base-unit selling price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Quoted line value at the company currency scale.</summary>
    public decimal NetValue { get; set; }

    public Quotation Quotation { get; set; } = null!;
}
