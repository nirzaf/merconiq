namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one cashier shift. Sales post only into open shifts.</summary>
public enum PosShiftStatus
{
    Open = 0,
    Closed = 1
}
