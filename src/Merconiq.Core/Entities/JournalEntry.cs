namespace Merconiq.Core.Entities;

/// <summary>
/// One posted, immutable general-ledger journal. Rows are append-only: corrections happen
/// through source-linked reversals, never through edits or deletes. One journal belongs to
/// exactly one committed posting action identified by the company/source tuple, and duplicate
/// submissions of the same action are answered from the committed journal instead of posting again.
/// </summary>
public sealed class JournalEntry : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string JournalNumber { get; set; } = string.Empty;

    /// <summary>Calendar posting date; must fall inside an open fiscal period at posting time.</summary>
    public DateOnly PostingDate { get; set; }

    /// <summary>Base currency of the owning company; journals post in base currency only.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>Reviewer-supplied description of the business action.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Business document family that produced this journal, for example "GoodsReceipt".</summary>
    public string SourceDocumentType { get; set; } = string.Empty;

    /// <summary>Stable identifier of the source document inside its family.</summary>
    public string SourceDocumentId { get; set; } = string.Empty;

    /// <summary>Named business action on the source document, for example "Post" or "Reverse".</summary>
    public string SourceAction { get; set; } = string.Empty;

    /// <summary>Distinguishes multiple legal postings from one source document, for example partial events.</summary>
    public int SourceVersion { get; set; } = 1;

    /// <summary>Deterministic hash of the committed request payload; conflicting retries are rejected.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    /// <summary>Set when this journal reverses an earlier posted journal; reversals are not reversible.</summary>
    public int? ReversalOfJournalEntryId { get; set; }

    /// <summary>Mandatory reviewer reason carried on reversal journals.</summary>
    public string? ReversalReason { get; set; }

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();

    public JournalEntry? ReversalOfJournalEntry { get; set; }
}
