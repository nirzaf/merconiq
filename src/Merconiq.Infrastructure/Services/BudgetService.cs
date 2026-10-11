using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Annual budgets per account and budget-versus-actual reads. Actuals come from posted ledger
/// lines (expenses: debits minus credits; income: credits minus debits); budgets never post.
/// </summary>
public sealed class BudgetService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork) : IBudgetService
{
    /// <inheritdoc />
    public async Task<Budget> SetBudgetAsync(
        SetBudgetRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.AnnualAmount < 0)
            throw new FinancePostingValidationException("The budgeted amount must not be negative.");

        Budget saved = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"budget:{request.CompanyId}", cancellationToken);
            var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ChartOfAccountId &&
                        candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Chart account not found in this company.");

            var budget = await context.Budgets.SingleOrDefaultAsync(
                candidate => candidate.CompanyId == request.CompanyId &&
                    candidate.ChartOfAccountId == request.ChartOfAccountId &&
                    candidate.Year == request.Year,
                cancellationToken);
            if (budget is null)
            {
                budget = new Budget
                {
                    CompanyId = request.CompanyId,
                    ChartOfAccountId = request.ChartOfAccountId,
                    Year = request.Year
                };
                context.Budgets.Add(budget);
            }
            budget.AnnualAmount = request.AnnualAmount;
            budget.Notes = request.Notes;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            saved = budget;
        }, cancellationToken: cancellationToken);
        return saved;
    }

    /// <inheritdoc />
    public async Task<BudgetVersusActual> GetBudgetVersusActualAsync(
        int companyId, int budgetId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var budget = await context.Budgets
            .Include(candidate => candidate.Account)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == budgetId && candidate.CompanyId == companyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Budget not found in this company.");

        var yearStart = new DateOnly(budget.Year, 1, 1);
        var yearEnd = new DateOnly(budget.Year, 12, 31);
        var lines = await context.JournalLines
            .Where(line => line.CompanyId == companyId &&
                line.ChartOfAccountId == budget.ChartOfAccountId &&
                line.JournalEntry.PostingDate >= yearStart &&
                line.JournalEntry.PostingDate <= yearEnd)
            .ToListAsync(cancellationToken);

        // Actuals respect the account class convention so income and expense budgets both read
        // as positive plan-versus-performance numbers.
        var actual = budget.Account.AccountClass switch
        {
            AccountClass.Income => lines.Sum(line => line.Credit - line.Debit),
            _ => lines.Sum(line => line.Debit - line.Credit)
        };
        return new BudgetVersusActual(
            budget.Id,
            budget.ChartOfAccountId,
            budget.Account.AccountCode,
            budget.Account.Name,
            budget.Year,
            budget.AnnualAmount,
            actual,
            actual - budget.AnnualAmount,
            actual > budget.AnnualAmount);
    }
}
