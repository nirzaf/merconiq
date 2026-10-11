namespace Merconiq.Core.Entities;

/// <summary>
/// One production order against a released BOM. Completion consumes components through the
/// stock ledger at moving-average cost, receives the finished quantity at the exact consumed
/// unit cost, and posts the paired WIP journals through the finance kernel — all atomically,
/// exactly once. Component consumption is snapshotted per line.
/// </summary>
public sealed class WorkOrder : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string WorkOrderNumber { get; set; } = string.Empty;

    public int BillOfMaterialsId { get; set; }

    public BillOfMaterials BillOfMaterials { get; set; } = null!;

    /// <summary>The finished item; must match the BOM's item.</summary>
    public int ItemId { get; set; }

    /// <summary>Quantity to produce, in base units.</summary>
    public int Quantity { get; set; }

    /// <summary>Production location: components issue from and finished goods arrive here.</summary>
    public int LocationId { get; set; }

    public Location Location { get; set; } = null!;

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;

    /// <summary>BOM version snapshotted at creation.</summary>
    public int BomVersion { get; set; }

    public DateOnly PostingDate { get; set; }

    public string? Notes { get; set; }

    /// <summary>Total component cost consumed, snapshotted at completion.</summary>
    public decimal ConsumedCost { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? CompletedBy { get; set; }

    public ICollection<WorkOrderConsumption> Consumptions { get; set; } = new List<WorkOrderConsumption>();
}
