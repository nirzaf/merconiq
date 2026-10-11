namespace Merconiq.Core.Entities;

public enum PortalType
{
    Customer = 1,
    Supplier = 2
}

/// <summary>
/// Restricted external portal contact authorized for customer or supplier self-service.
/// </summary>
public sealed class PortalContact : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public PortalType PortalType { get; set; }
    public int PartyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? InviteToken { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }
}

public enum SupplierCollaborationType
{
    Acknowledgment = 1,
    DeliveryUpdate = 2,
    InvoiceReferenceSubmission = 3
}

public enum CollaborationStatus
{
    PendingBuyerReview = 1,
    Accepted = 2,
    Rejected = 3
}

/// <summary>
/// Supplier collaboration record (acknowledgment, ETA update, or invoice submission)
/// stored as unposted input requiring buyer/AP review.
/// </summary>
public sealed class SupplierCollaborationEntry : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int SupplierId { get; set; }
    public int PurchaseOrderId { get; set; }
    public SupplierCollaborationType EntryType { get; set; }
    public string? SupplierReference { get; set; }
    public DateOnly? ProposedDeliveryDate { get; set; }
    public string? InvoiceReferenceNumber { get; set; }
    public decimal? InvoiceAmount { get; set; }
    public CollaborationStatus Status { get; set; } = CollaborationStatus.PendingBuyerReview;
    public string? BuyerReviewNotes { get; set; }
}
