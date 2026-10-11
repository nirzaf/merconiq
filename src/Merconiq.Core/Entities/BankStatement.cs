namespace Merconiq.Core.Entities;

/// <summary>
/// One imported bank statement for one company bank account on one statement date. Statements are
/// evidence: they are imported once and never edited; corrections use a new statement for the date.
/// The opening balance plus every line must equal the closing balance, checked at import.
/// </summary>
public sealed class BankStatement : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>The company's chart account that mirrors the bank account in the general ledger.</summary>
    public int ChartOfAccountId { get; set; }

    /// <summary>Statement cut-off date; ledger lines posted after it are never matched to this statement.</summary>
    public DateOnly StatementDate { get; set; }

    public decimal OpeningBalance { get; set; }

    public decimal ClosingBalance { get; set; }

    /// <summary>Optional bank-supplied statement reference, kept for audit.</summary>
    public string? Reference { get; set; }

    public string ImportedBy { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; }

    public ICollection<BankStatementLine> Lines { get; set; } = new List<BankStatementLine>();
}
