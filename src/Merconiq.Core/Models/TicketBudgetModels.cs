using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Models;

/// <summary>Creates one service ticket.</summary>
public sealed record CreateTicketRequest(
    int CompanyId,
    string Subject,
    string? Description,
    TicketPriority Priority,
    int? ProjectId = null);

/// <summary>Sets one annual budget for an account.</summary>
public sealed record SetBudgetRequest(
    int CompanyId,
    int ChartOfAccountId,
    int Year,
    decimal AnnualAmount,
    string? Notes = null);

/// <summary>Budget versus posted ledger actuals for one account and year.</summary>
public sealed record BudgetVersusActual(
    int BudgetId,
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    int Year,
    decimal BudgetedAmount,
    decimal ActualAmount,
    decimal Variance,
    bool OverBudget);
