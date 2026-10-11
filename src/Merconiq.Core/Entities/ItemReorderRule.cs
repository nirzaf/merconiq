namespace Merconiq.Core.Entities;

/// <summary>
/// Safety stock and automated reorder trigger rule for an item at a specific location.
/// </summary>
public sealed class ItemReorderRule : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public int ReorderLevel { get; set; }
    public int ReorderQuantity { get; set; }
    public bool IsActive { get; set; } = true;
}
