namespace Merconiq.Core.Entities;

/// <summary>
/// Reviewer-confirmed link between one card tender and the settlement batch that paid it. A batch may pay
/// many tenders, but a tender is paid by at most one batch at a time; withdrawn matches stay as history.
/// </summary>
public sealed class CardSettlementMatch : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int CardSettlementBatchId { get; set; }

    public int PosSaleTenderId { get; set; }

    /// <summary>The tender amount that this batch settled; equals the tender's amount when the match is made.</summary>
    public decimal Amount { get; set; }

    public bool IsActive { get; set; } = true;

    public string MatchedBy { get; set; } = string.Empty;

    public DateTime MatchedAtUtc { get; set; }

    public string? UnmatchedBy { get; set; }

    public DateTime? UnmatchedAtUtc { get; set; }

    public string? UnmatchReason { get; set; }

    public CardSettlementBatch CardSettlementBatch { get; set; } = null!;

    public PosSaleTender PosSaleTender { get; set; } = null!;
}
