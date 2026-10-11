namespace Merconiq.Core.Entities;

/// <summary>
/// Pre-approved intercompany trading relationship between two companies in the same tenant.
/// </summary>
public sealed class IntercompanyRelationship : AuditableEntity
{
    public int Id { get; set; }
    public int ParentCompanyId { get; set; }
    public int SubsidiaryCompanyId { get; set; }
    public int ParentReceivableAccountId { get; set; }
    public int ParentRevenueAccountId { get; set; }
    public int SubsidiaryPayableAccountId { get; set; }
    public int SubsidiaryExpenseAccountId { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum IntercompanyTransactionStatus
{
    Pending = 1,
    Reconciled = 2,
    Rejected = 3
}

/// <summary>
/// Linked intercompany document transaction with stable reciprocal reference key.
/// </summary>
public sealed class IntercompanyTransaction : AuditableEntity
{
    public int Id { get; set; }
    public int RelationshipId { get; set; }
    public int SourceCompanyId { get; set; }
    public int TargetCompanyId { get; set; }
    public string SourceDocumentType { get; set; } = "SalesInvoice";
    public int SourceDocumentId { get; set; }
    public string TargetDocumentType { get; set; } = "PurchaseInvoice";
    public int? TargetDocumentId { get; set; }
    public string ReferenceKey { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public IntercompanyTransactionStatus Status { get; set; } = IntercompanyTransactionStatus.Pending;
    public string? Notes { get; set; }
}
