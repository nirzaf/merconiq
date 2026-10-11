namespace Merconiq.Core.Entities;

/// <summary>
/// First-In-First-Out valuation layer tracking inward lots with remaining quantities and unit costs.
/// </summary>
public sealed class StockFifoLayer : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public DateTime InwardDateUtc { get; set; }
    public int RemainingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? SourceReference { get; set; }
}
