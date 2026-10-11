namespace Merconiq.Core.Entities;

/// <summary>
/// One controllable fiscal period inside a fiscal year. Closing is an approved administrative
/// action; reopening requires a recorded reason and separate authorization, and both transitions
/// are captured by the audit log.
/// </summary>
public sealed class FiscalPeriod : AuditableEntity
{
    public int Id { get; set; }

    public int FiscalYearId { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Ordinal inside the fiscal year, starting at 1.</summary>
    public int PeriodNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Inclusive first calendar day of the period.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive last calendar day of the period.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Whether this is the year-end adjustment period (period 13). Adjustment periods share the fiscal
    /// year's last day, never claim a posting date on their own, and accept only journals explicitly
    /// targeted at them, so year-end corrections can post after the regular December period closes.
    /// </summary>
    public bool IsAdjustment { get; set; }

    public FiscalPeriodStatus Status { get; set; } = FiscalPeriodStatus.Open;

    public DateTime? ClosedAtUtc { get; set; }

    public string? ClosedBy { get; set; }

    public DateTime? ReopenedAtUtc { get; set; }

    public string? ReopenedBy { get; set; }

    /// <summary>Mandatory reviewer-supplied reason recorded when a closed period is reopened.</summary>
    public string? ReopenReason { get; set; }

    public FiscalYear FiscalYear { get; set; } = null!;
}
