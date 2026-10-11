namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one work order. Completion is terminal and posts exactly once.</summary>
public enum WorkOrderStatus
{
    Draft = 0,
    Released = 1,
    Completed = 2,
    Cancelled = 3
}
