namespace Merconiq.Core.Entities;

/// <summary>
/// One recorded work line against a project task. Draft/Submitted lines are editable by their
/// owner; approval freezes the line (hours, rate, billable flag) as immutable evidence for
/// profitability reads. Rejected lines are closed evidence and cannot be resubmitted.
/// </summary>
public sealed class TimesheetEntry : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public int ProjectTaskId { get; set; }

    public ProjectTask ProjectTask { get; set; } = null!;

    /// <summary>The user identifier whose time this records.</summary>
    public string WorkerId { get; set; } = string.Empty;

    public DateOnly WorkDate { get; set; }

    /// <summary>Worked hours in 0.25 increments, greater than zero.</summary>
    public decimal Hours { get; set; }

    public bool Billable { get; set; }

    /// <summary>Hourly rate snapshotted at entry; used for profitability reads.</summary>
    public decimal HourlyRate { get; set; }

    public TimesheetEntryStatus Status { get; set; } = TimesheetEntryStatus.Draft;

    public string? Notes { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public string? ApprovedBy { get; set; }

    public string? RejectionReason { get; set; }
}
