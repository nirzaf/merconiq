namespace Merconiq.Core.Entities;

/// <summary>One component line: quantity of the component per one produced base unit.</summary>
public sealed class BillOfMaterialsLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int BillOfMaterialsId { get; set; }

    public BillOfMaterials BillOfMaterials { get; set; } = null!;

    public int ComponentItemId { get; set; }

    public Item ComponentItem { get; set; } = null!;

    /// <summary>Component quantity in base units per one produced base unit; positive.</summary>
    public decimal QuantityPerUnit { get; set; }

    public int LineNumber { get; set; }
}
