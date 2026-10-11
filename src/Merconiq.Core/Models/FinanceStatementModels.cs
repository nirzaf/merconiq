namespace Merconiq.Core.Models;

/// <summary>One statement line: an account and its window balance.</summary>
public sealed record StatementLine(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    decimal Amount);

/// <summary>Ledger-backed income statement for a posting-date window.</summary>
public sealed record ProfitAndLossStatement(
    int CompanyId,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<StatementLine> Income,
    IReadOnlyList<StatementLine> Expenses,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetProfit);

/// <summary>Ledger-backed balance sheet as of a date, including retained profit; must balance.</summary>
public sealed record BalanceSheetStatement(
    int CompanyId,
    DateOnly AsOfDate,
    IReadOnlyList<StatementLine> Assets,
    IReadOnlyList<StatementLine> Liabilities,
    IReadOnlyList<StatementLine> Equity,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal NetProfit,
    bool Balances);
