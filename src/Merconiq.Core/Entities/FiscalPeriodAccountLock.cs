namespace Merconiq.Core.Entities;

/// <summary>
/// Per-period posting lock for one posting chart account. While locked, no journal may touch the
/// account on a posting date inside the fiscal period, including reversals. One row exists per
/// company, period and account; releasing a lock keeps the row and records the release, so the
/// current state and the last release stay visible while the audit log keeps the full history.
/// </summary>
public sealed class FiscalPeriodAccountLock : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int FiscalPeriodId { get; set; }

    public int ChartOfAccountId { get; set; }

    /// <summary>Whether the account currently rejects postings inside the period.</summary>
    public bool IsLocked { get; set; }

    public DateTime LockedAtUtc { get; set; }

    public string LockedBy { get; set; } = string.Empty;

    /// <summary>Mandatory reviewer reason recorded when the lock is applied.</summary>
    public string LockReason { get; set; } = string.Empty;

    public DateTime? ReleasedAtUtc { get; set; }

    public string? ReleasedBy { get; set; }

    /// <summary>Mandatory reviewer reason recorded when the lock is released.</summary>
    public string? ReleaseReason { get; set; }

    public FiscalPeriod FiscalPeriod { get; set; } = null!;

    public ChartOfAccount ChartOfAccount { get; set; } = null!;
}
