namespace Merconiq.Core.Entities;

/// <summary>
/// One leave request with its inclusive date range. Approval requires the Approve capability,
/// records the approver, and is rejected if approved leave already overlaps the range.
/// </summary>
public sealed class LeaveRequest : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public LeaveType LeaveType { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    /// <summary>Working days requested (inclusive of both ends, weekends excluded).</summary>
    public decimal Days { get; set; }

    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;

    /// <summary>Employee-supplied reason; mandatory with the request.</summary>
    public string Reason { get; set; } = string.Empty;

    public DateTime? DecidedAtUtc { get; set; }

    public string? DecidedBy { get; set; }

    public string? DecisionReason { get; set; }
}
