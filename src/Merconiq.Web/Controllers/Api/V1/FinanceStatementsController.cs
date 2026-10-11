using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>
/// Ledger-backed financial statements: income statement for a posting window and balance sheet
/// as of a date. Read-only projections of immutable journal lines against classified accounts.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/statements")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class FinanceStatementsController(
    IFinanceStatementService statements,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Income statement for the posting-date window.</summary>
    [HttpGet("profit-and-loss")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetProfitAndLoss(
        int companyId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var statement = await statements.GetProfitAndLossAsync(companyId, fromDate, toDate, cancellationToken);
        return Ok(ApiResponse<ProfitAndLossResponse>.CreateSuccess(new ProfitAndLossResponse(
            statement.CompanyId,
            statement.FromDate,
            statement.ToDate,
            statement.Income.Select(ToLine).ToArray(),
            statement.Expenses.Select(ToLine).ToArray(),
            statement.TotalIncome,
            statement.TotalExpenses,
            statement.NetProfit,
            statement.Rollups.Select(ToRollup).ToArray())));
    }

    /// <summary>Balance sheet as of a date, including retained profit; must balance.</summary>
    [HttpGet("balance-sheet")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetBalanceSheet(
        int companyId,
        [FromQuery] DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var statement = await statements.GetBalanceSheetAsync(companyId, asOfDate, cancellationToken);
        return Ok(ApiResponse<BalanceSheetResponse>.CreateSuccess(new BalanceSheetResponse(
            statement.CompanyId,
            statement.AsOfDate,
            statement.Assets.Select(ToLine).ToArray(),
            statement.Liabilities.Select(ToLine).ToArray(),
            statement.Equity.Select(ToLine).ToArray(),
            statement.TotalAssets,
            statement.TotalLiabilities,
            statement.TotalEquity,
            statement.NetProfit,
            statement.Balances,
            statement.Rollups.Select(ToRollup).ToArray())));
    }

    private static StatementLineResponse ToLine(StatementLine line) => new(
        line.ChartOfAccountId, line.AccountCode, line.AccountName, line.Amount);

    private static StatementRollupResponse ToRollup(StatementRollupLine line) => new(
        line.ChartOfAccountId,
        line.AccountCode,
        line.AccountName,
        line.AccountClass.ToString(),
        line.ParentChartOfAccountId,
        line.Depth,
        line.Amount);
}

public sealed record StatementLineResponse(
    int ChartOfAccountId, string AccountCode, string AccountName, decimal Amount);

/// <summary>
/// Classified group account with the balance of its posted descendants in the same class.
/// Depth 0 is a top-level group; rows are subtotals and never represent postings themselves.
/// </summary>
public sealed record StatementRollupResponse(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    string AccountClass,
    int? ParentChartOfAccountId,
    int Depth,
    decimal Amount);

public sealed record ProfitAndLossResponse(
    int CompanyId,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<StatementLineResponse> Income,
    IReadOnlyList<StatementLineResponse> Expenses,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetProfit,
    IReadOnlyList<StatementRollupResponse> Rollups);

public sealed record BalanceSheetResponse(
    int CompanyId,
    DateOnly AsOfDate,
    IReadOnlyList<StatementLineResponse> Assets,
    IReadOnlyList<StatementLineResponse> Liabilities,
    IReadOnlyList<StatementLineResponse> Equity,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal NetProfit,
    bool Balances,
    IReadOnlyList<StatementRollupResponse> Rollups);
