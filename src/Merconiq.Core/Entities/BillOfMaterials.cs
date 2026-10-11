namespace Merconiq.Core.Entities;

/// <summary>
/// One versioned bill of materials for a finished item: the component quantities consumed per
/// one produced base unit. One active version per item; work orders snapshot the released BOM.
/// </summary>
public sealed class BillOfMaterials : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>The finished item this BOM produces.</summary>
    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>Version label, unique per company and item.</summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public ICollection<BillOfMaterialsLine> Lines { get; set; } = new List<BillOfMaterialsLine>();
}
