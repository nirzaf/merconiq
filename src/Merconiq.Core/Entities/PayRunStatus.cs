namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one payroll run. Posting is the terminal, one-time transition.</summary>
public enum PayRunStatus
{
    Draft = 0,
    Posted = 1
}
