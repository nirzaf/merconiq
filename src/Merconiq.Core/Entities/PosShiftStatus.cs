namespace Merconiq.Core.Entities;

/// <summary>
/// Lifecycle of one cashier shift. Sales post only into open shifts. A close whose cash variance is above
/// the register's approval threshold waits in PendingApproval until a reviewer approves or rejects it.
/// </summary>
public enum PosShiftStatus
{
    Open = 0,
    Closed = 1,
    PendingApproval = 2
}
