namespace Merconiq.Core.Entities;

/// <summary>
/// One bank movement on a statement. A positive amount is money into the bank account and a negative
/// amount is money out, matching the sign of the ledger's debit minus credit on the bank account.
/// </summary>
public sealed class BankStatementLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int BankStatementId { get; set; }

    /// <summary>One-based ordinal inside the statement, in bank order.</summary>
    public int LineNumber { get; set; }

    public DateOnly ValueDate { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Reference { get; set; }

    public decimal Amount { get; set; }

    public BankStatement BankStatement { get; set; } = null!;
}
