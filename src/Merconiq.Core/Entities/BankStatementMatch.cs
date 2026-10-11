namespace Merconiq.Core.Entities;

/// <summary>
/// Reviewer-confirmed link between one bank statement line and one ledger line on the bank account.
/// At most one active match exists per statement line and per ledger line. Unmatching deactivates the
/// row and records the reason, so the reconciliation history stays intact.
/// </summary>
public sealed class BankStatementMatch : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int BankStatementLineId { get; set; }

    public int JournalLineId { get; set; }

    public bool IsActive { get; set; } = true;

    public string MatchedBy { get; set; } = string.Empty;

    public DateTime MatchedAtUtc { get; set; }

    public string? UnmatchedBy { get; set; }

    public DateTime? UnmatchedAtUtc { get; set; }

    /// <summary>Mandatory reviewer reason recorded when a match is withdrawn.</summary>
    public string? UnmatchReason { get; set; }

    public BankStatementLine BankStatementLine { get; set; } = null!;

    public JournalLine JournalLine { get; set; } = null!;
}
