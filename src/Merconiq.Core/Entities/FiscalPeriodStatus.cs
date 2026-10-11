namespace Merconiq.Core.Entities;

/// <summary>Posting gate for one fiscal period. Closed periods reject new journal postings.</summary>
public enum FiscalPeriodStatus
{
    Open = 0,
    Closed = 1
}
