namespace Merconiq.Core.Entities;

/// <summary>
/// One component consumption snapshot: the quantity issued from stock and the exact
/// moving-average value the issue booked. Immutable once the work order completes.
/// </summary>
public sealed class WorkOrderConsumption : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int WorkOrderId { get; set; }

    public WorkOrder WorkOrder { get; set; } = null!;

    public int ComponentItemId { get; set; }

    /// <summary>Issued quantity in base units.</summary>
    public int Quantity { get; set; }

    /// <summary>The stock transaction that issued this quantity.</summary>
    public int StockTransactionId { get; set; }

    /// <summary>Exact issued value from the stock valuation entry.</summary>
    public decimal ConsumedValue { get; set; }
}
