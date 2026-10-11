namespace Merconiq.Core.Entities;

/// <summary>
/// One delivered line against a sales-order line. Delivered quantity is in base units and is
/// capped by the outstanding ordered quantity. The linked stock transaction carries the exact
/// moving-average cost issued, which is what the COGS journal books.
/// </summary>
public sealed class DeliveryNoteLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int DeliveryNoteId { get; set; }

    public int LineNumber { get; set; }

    public int SalesOrderLineId { get; set; }

    public SalesOrderLine SalesOrderLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Quantity issued from stock, in base units.</summary>
    public int Quantity { get; set; }

    /// <summary>The stock transaction that issued this quantity; the COGS source of truth.</summary>
    public int StockTransactionId { get; set; }

    public StockTransaction StockTransaction { get; set; } = null!;

    public string? BatchNumber { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public DeliveryNote DeliveryNote { get; set; } = null!;
}
