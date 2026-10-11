namespace Merconiq.Core.Entities;

/// <summary>
/// One employee expense claim. The claimant is recorded as a name because the company has no employee master
/// yet; the claim's money effects are journals posted by approval and payment, never stored totals.
/// </summary>
public sealed class ExpenseClaim : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-allocated claim number from the document sequence.</summary>
    public string ClaimNumber { get; set; } = string.Empty;

    public string ClaimantName { get; set; } = string.Empty;

    public DateOnly ClaimDate { get; set; }

    public string Description { get; set; } = string.Empty;

    public ExpenseClaimStatus Status { get; set; } = ExpenseClaimStatus.Draft;

    public decimal TotalAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedByUser { get; set; } = string.Empty;

    public DateTime? SubmittedAtUtc { get; set; }

    public string? SubmittedBy { get; set; }

    public int? ApprovalJournalEntryId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTime? RejectedAtUtc { get; set; }

    public string? RejectedBy { get; set; }

    /// <summary>Mandatory reviewer reason recorded with a rejection.</summary>
    public string? RejectionReason { get; set; }

    public int? PaymentJournalEntryId { get; set; }

    public DateOnly? PaymentDate { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public string? PaidBy { get; set; }

    public ICollection<ExpenseClaimLine> Lines { get; set; } = new List<ExpenseClaimLine>();
}
