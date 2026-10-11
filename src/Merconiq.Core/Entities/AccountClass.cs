namespace Merconiq.Core.Entities;

/// <summary>
/// Statement classification for one chart account. Unlike the free-form company-supplied
/// AccountType label, this value drives P&amp;L and balance-sheet presentation; posted accounts
/// without a class cannot appear in financial statements.
/// </summary>
public enum AccountClass
{
    Asset = 0,
    Liability = 1,
    Equity = 2,
    Income = 3,
    Expense = 4
}
