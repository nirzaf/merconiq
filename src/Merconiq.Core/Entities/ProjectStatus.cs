namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one project. Closed projects reject new timesheets.</summary>
public enum ProjectStatus
{
    Active = 0,
    Completed = 1,
    Closed = 2
}
