namespace Merconiq.Core.Entities;

public enum MaterialRequestPurpose
{
    Purchase = 1,
    Transfer = 2,
    Manufacture = 3
}

public enum MaterialRequestStatus
{
    Draft = 1,
    Approved = 2,
    Ordered = 3,
    Cancelled = 4
}

/// <summary>
/// Internal requisition for material or items before purchasing or transferring.
/// </summary>
public sealed class MaterialRequest : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public DateOnly RequestedDate { get; set; }
    public DateOnly? RequiredDate { get; set; }
    public MaterialRequestPurpose Purpose { get; set; } = MaterialRequestPurpose.Purchase;
    public MaterialRequestStatus Status { get; set; } = MaterialRequestStatus.Draft;
    public string RequestedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public ICollection<MaterialRequestLine> Lines { get; set; } = new List<MaterialRequestLine>();
}

public sealed class MaterialRequestLine : AuditableEntity
{
    public int Id { get; set; }
    public int MaterialRequestId { get; set; }
    public MaterialRequest MaterialRequest { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int Quantity { get; set; }
    public int OrderedQuantity { get; set; }
}

public enum SupplierQuotationStatus
{
    Draft = 1,
    Submitted = 2,
    Accepted = 3,
    Rejected = 4
}

/// <summary>
/// Quotation received from a prospective or active supplier for price comparison.
/// </summary>
public sealed class SupplierQuotation : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public DateOnly QuotationDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public SupplierQuotationStatus Status { get; set; } = SupplierQuotationStatus.Draft;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Notes { get; set; }

    public ICollection<SupplierQuotationLine> Lines { get; set; } = new List<SupplierQuotationLine>();
}

public sealed class SupplierQuotationLine : AuditableEntity
{
    public int Id { get; set; }
    public int SupplierQuotationId { get; set; }
    public SupplierQuotation SupplierQuotation { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal TotalAmount { get; set; }
}
