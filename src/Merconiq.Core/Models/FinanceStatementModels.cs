using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One statement line: an account and its window balance.</summary>
public sealed record StatementLine(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    decimal Amount);

/// <summary>
/// One group account in the chart hierarchy with the balance of every posted descendant leaf
/// account in the same statement class. Group rows are presentation subtotals; they never post.
/// </summary>
public sealed record StatementRollupLine(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    AccountClass AccountClass,
    int? ParentChartOfAccountId,
    int Depth,
    decimal Amount);

/// <summary>Chart-of-accounts node used to walk the group hierarchy for rollups.</summary>
public sealed record StatementAccountNode(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    int? ParentChartOfAccountId,
    bool IsGroupAccount,
    AccountClass? AccountClass);

/// <summary>One posted, already signed (normal-balance positive) amount for a classified leaf account.</summary>
public sealed record StatementPostingRow(
    int ChartOfAccountId,
    AccountClass AccountClass,
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
    decimal NetProfit,
    IReadOnlyList<StatementRollupLine> Rollups);

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
    bool Balances,
    IReadOnlyList<StatementRollupLine> Rollups);
