namespace Merconiq.Core.Entities;

/// <summary>One expense on a claim, posted to its chosen expense account when the claim is approved.</summary>
public sealed class ExpenseClaimLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int ExpenseClaimId { get; set; }

    /// <summary>One-based ordinal inside the claim.</summary>
    public int LineNumber { get; set; }

    public int ExpenseChartOfAccountId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Amount in base currency; always positive.</summary>
    public decimal Amount { get; set; }

    public ExpenseClaim ExpenseClaim { get; set; } = null!;
}
