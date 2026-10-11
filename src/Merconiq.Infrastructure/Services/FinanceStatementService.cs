using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Builds the income statement and balance sheet directly from posted ledger lines joined to
/// classified chart accounts. Statements are pure read projections: no mutable totals, no
/// unclassified accounts, and the balance sheet fails loudly if it does not balance. Group
/// accounts carry hierarchy rollups computed from the same posted lines.
/// </summary>
public sealed class FinanceStatementService(
    InventoryDbContext context) : IFinanceStatementService
{
    private static readonly AccountClass[] IncomeStatementClasses = [AccountClass.Income, AccountClass.Expense];
    private static readonly AccountClass[] BalanceSheetClasses = [AccountClass.Asset, AccountClass.Liability, AccountClass.Equity];

    /// <inheritdoc />
    public async Task<ProfitAndLossStatement> GetProfitAndLossAsync(
        int companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        if (fromDate > toDate)
            throw new FinancePostingValidationException("The statement start date must not be after its end date.");

        var rows = await QueryClassifiedRowsAsync(companyId, cancellationToken);
        RequireClassified(rows, "profit-and-loss");
        var windowed = rows
            .Where(row => row.PostingDate >= fromDate && row.PostingDate <= toDate)
            .ToList();

        var income = Aggregate(windowed, AccountClass.Income, creditPositive: true);
        var expenses = Aggregate(windowed, AccountClass.Expense, creditPositive: false);
        var totalIncome = income.Sum(line => line.Amount);
        var totalExpenses = expenses.Sum(line => line.Amount);
        var hierarchy = await QueryAccountHierarchyAsync(companyId, cancellationToken);
        var rollups = StatementRollupCalculator.Build(hierarchy, SignedPostings(windowed), IncomeStatementClasses);
        return new ProfitAndLossStatement(
            companyId, fromDate, toDate, income, expenses, totalIncome, totalExpenses,
            totalIncome - totalExpenses, rollups);
    }

    /// <inheritdoc />
    public async Task<BalanceSheetStatement> GetBalanceSheetAsync(
        int companyId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var rows = await QueryClassifiedRowsAsync(companyId, cancellationToken);
        RequireClassified(rows, "balance-sheet");
        var asOf = rows.Where(row => row.PostingDate <= asOfDate).ToList();

        var assets = Aggregate(asOf, AccountClass.Asset, creditPositive: false);
        var liabilities = Aggregate(asOf, AccountClass.Liability, creditPositive: true);
        var equity = Aggregate(asOf, AccountClass.Equity, creditPositive: true);
        var totalAssets = assets.Sum(line => line.Amount);
        var totalLiabilities = liabilities.Sum(line => line.Amount);
        var totalEquity = equity.Sum(line => line.Amount);
        // Profit is income minus expenses; sum the two classes separately.
        var totalIncome = asOf.Where(row => row.Class == AccountClass.Income)
            .Sum(row => row.Credit - row.Debit);
        var totalExpenses = asOf.Where(row => row.Class == AccountClass.Expense)
            .Sum(row => row.Debit - row.Credit);
        var netProfit = totalIncome - totalExpenses;

        // Retained profit belongs to the equity side; the sheet must balance exactly.
        var balances = totalAssets == totalLiabilities + totalEquity + netProfit;
        if (!balances)
        {
            throw new InvalidOperationException(
                $"Balance sheet for company {companyId} does not balance as of {asOfDate:yyyy-MM-dd}: " +
                $"assets {totalAssets}, liabilities {totalLiabilities}, equity {totalEquity}, profit {netProfit}.");
        }

        var hierarchy = await QueryAccountHierarchyAsync(companyId, cancellationToken);
        var rollups = StatementRollupCalculator.Build(hierarchy, SignedPostings(asOf), BalanceSheetClasses);
        return new BalanceSheetStatement(
            companyId, asOfDate, assets, liabilities, equity,
            totalAssets, totalLiabilities, totalEquity, netProfit, balances, rollups);
    }

    private async Task<List<LedgerRow>> QueryClassifiedRowsAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        await context.JournalLines
            .Where(line => line.CompanyId == companyId)
            .Join(context.ChartOfAccounts.Where(account => account.CompanyId == companyId),
                line => line.ChartOfAccountId,
                account => account.Id,
                (line, account) => new LedgerRow
                {
                    PostingDate = line.JournalEntry.PostingDate,
                    Class = account.AccountClass,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    AccountId = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.Name
                })
            .ToListAsync(cancellationToken);

    private async Task<List<StatementAccountNode>> QueryAccountHierarchyAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        await context.ChartOfAccounts
            .Where(account => account.CompanyId == companyId)
            .Select(account => new StatementAccountNode(
                account.Id,
                account.AccountCode,
                account.Name,
                account.ParentAccountId,
                account.IsGroupAccount,
                account.AccountClass))
            .ToListAsync(cancellationToken);

    private static List<StatementLine> Aggregate(
        IReadOnlyList<LedgerRow> rows,
        AccountClass accountClass,
        bool creditPositive) =>
        rows.Where(row => row.Class == accountClass)
            .GroupBy(row => new { row.AccountId, row.AccountCode, row.AccountName })
            .Select(group => new StatementLine(
                group.Key.AccountId,
                group.Key.AccountCode,
                group.Key.AccountName,
                group.Sum(row => creditPositive ? row.Credit - row.Debit : row.Debit - row.Credit)))
            .OrderBy(line => line.AccountCode)
            .ToList();

    /// <summary>Projects classified lines to normal-balance amounts; rollups sum these per group.</summary>
    private static List<StatementPostingRow> SignedPostings(IReadOnlyList<LedgerRow> rows) =>
        rows.Where(row => row.Class is not null)
            .Select(row => new StatementPostingRow(
                row.AccountId,
                row.Class!.Value,
                row.Class!.Value is AccountClass.Asset or AccountClass.Expense
                    ? row.Debit - row.Credit
                    : row.Credit - row.Debit))
            .ToList();

    private static void RequireClassified(IReadOnlyList<LedgerRow> rows, string statement)
    {
        var unclassified = rows.Where(row => row.Class is null)
            .Select(row => $"{row.AccountCode} {row.AccountName}")
            .Distinct()
            .ToList();
        if (unclassified.Count > 0)
        {
            throw new FinancePostingValidationException(
                $"Posted accounts lack a statement classification; configure AccountClass before building " +
                $"the {statement}: {string.Join(", ", unclassified)}.");
        }
    }

    private sealed class LedgerRow
    {
        public DateOnly PostingDate { get; init; }
        public AccountClass? Class { get; init; }
        public decimal Debit { get; init; }
        public decimal Credit { get; init; }
        public int AccountId { get; init; }
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
    }
}
