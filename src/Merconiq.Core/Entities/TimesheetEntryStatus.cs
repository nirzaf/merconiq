namespace Merconiq.Core.Entities;

/// <summary>Approval state of one timesheet line. Approved lines are immutable evidence.</summary>
public enum TimesheetEntryStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3
}
