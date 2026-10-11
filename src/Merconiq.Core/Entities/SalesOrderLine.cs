namespace Merconiq.Core.Entities;

/// <summary>
/// One ordered line. Prices are snapshotted at approval; later item or price edits never rewrite
/// the commitment.
/// </summary>
public sealed class SalesOrderLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SalesOrderId { get; set; }

    public int LineNumber { get; set; }

    public int ItemId { get; set; }

    /// <summary>Ordered quantity in base stock units.</summary>
    public int Quantity { get; set; }

    /// <summary>Approved per-base-unit selling price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Approved line net value (quantity times price at the order currency scale).</summary>
    public decimal NetValue { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
}
