namespace Merconiq.Core.Entities;

/// <summary>
/// One immutable general-ledger line. Exactly one of debit or credit is positive, and amounts
/// are stored at the owning company's declared currency scale. Account codes and names are
/// snapshotted so later master-data edits never rewrite financial history.
/// </summary>
public sealed class JournalLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int JournalEntryId { get; set; }

    /// <summary>One-based line ordinal inside the journal.</summary>
    public int LineNumber { get; set; }

    public int ChartOfAccountId { get; set; }

    public string AccountCodeSnapshot { get; set; } = string.Empty;

    public string AccountNameSnapshot { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Debit { get; set; }

    public decimal Credit { get; set; }

    public JournalEntry JournalEntry { get; set; } = null!;
}
