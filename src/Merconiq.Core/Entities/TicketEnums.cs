namespace Merconiq.Core.Entities;

/// <summary>Priority drives the resolution SLA clock.</summary>
public enum TicketPriority
{
    Normal = 0,
    High = 1,
    Critical = 2
}

/// <summary>Lifecycle of one service ticket. Resolution and closure stamp the SLA evidence.</summary>
public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3
}
