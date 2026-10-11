namespace Merconiq.Core.Entities;

/// <summary>
/// Effective-dated exchange rate between currency pairs for a company.
/// </summary>
public sealed class ExchangeRate : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public decimal Rate { get; set; }
}

public enum FxRevaluationStatus
{
    Draft = 1,
    Posted = 2,
    Reversed = 3
}

/// <summary>
/// Period-end unrealized foreign currency revaluation run.
/// </summary>
public sealed class FxRevaluationRun : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public DateOnly RevaluationDate { get; set; }
    public int FiscalPeriodId { get; set; }
    public FxRevaluationStatus Status { get; set; } = FxRevaluationStatus.Draft;
    public decimal TotalUnrealizedGain { get; set; }
    public decimal TotalUnrealizedLoss { get; set; }
    public int? JournalEntryId { get; set; }
    public int? ReversalJournalEntryId { get; set; }
}
