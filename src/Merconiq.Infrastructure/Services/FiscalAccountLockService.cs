using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Applies and releases per-period account locks. Both directions run under the company's
/// finance-period advisory lock, the same family the posting kernel takes, so a lock is either
/// fully visible to a journal's validation or applied after that journal has committed.
/// </summary>
public sealed class FiscalAccountLockService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<FiscalAccountLockService> logger) : IFiscalAccountLockService
{
    /// <inheritdoc />
    public async Task<FiscalPeriodAccountLock> LockAccountAsync(
        int companyId,
        int periodId,
        int chartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        FiscalPeriodAccountLock locked = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-period:{companyId}", cancellationToken);

            var period = await FindPeriodAsync(companyId, periodId, cancellationToken);
            await RequirePostingAccountAsync(companyId, chartOfAccountId, cancellationToken);

            var existing = await FindLockAsync(companyId, period.Id, chartOfAccountId, cancellationToken);
            if (existing is { IsLocked: true })
                throw new FinancePostingConflictException(
                    "This chart account is already locked for the fiscal period.");

            if (existing is null)
            {
                existing = new FiscalPeriodAccountLock
                {
                    CompanyId = companyId,
                    FiscalPeriodId = period.Id,
                    ChartOfAccountId = chartOfAccountId
                };
                context.FiscalPeriodAccountLocks.Add(existing);
            }

            existing.IsLocked = true;
            existing.LockedAtUtc = DateTime.UtcNow;
            existing.LockedBy = actor.Trim();
            existing.LockReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            locked = existing;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Locked chart account {ChartOfAccountId} for fiscal period {PeriodId} of company {CompanyId}",
            chartOfAccountId, periodId, companyId);
        return locked;
    }

    /// <inheritdoc />
    public async Task<FiscalPeriodAccountLock> ReleaseAccountAsync(
        int companyId,
        int periodId,
        int chartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        FiscalPeriodAccountLock released = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-period:{companyId}", cancellationToken);

            var period = await FindPeriodAsync(companyId, periodId, cancellationToken);
            var existing = await FindLockAsync(companyId, period.Id, chartOfAccountId, cancellationToken);
            if (existing is not { IsLocked: true })
                throw new FinancePostingConflictException(
                    "This chart account is not locked for the fiscal period.");

            existing.IsLocked = false;
            existing.ReleasedAtUtc = DateTime.UtcNow;
            existing.ReleasedBy = actor.Trim();
            existing.ReleaseReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            released = existing;
        }, cancellationToken: cancellationToken);

        logger.LogWarning(
            "Released chart account {ChartOfAccountId} lock for fiscal period {PeriodId} of company {CompanyId}; release reason recorded",
            chartOfAccountId, periodId, companyId);
        return released;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FiscalPeriodAccountLock>> ListLocksAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        await FindPeriodAsync(companyId, periodId, cancellationToken);

        return await context.FiscalPeriodAccountLocks
            .Where(entry => entry.CompanyId == companyId && entry.FiscalPeriodId == periodId)
            .OrderBy(entry => entry.ChartOfAccountId)
            .ToListAsync(cancellationToken);
    }

    private Task<FiscalPeriodAccountLock?> FindLockAsync(
        int companyId,
        int periodId,
        int chartOfAccountId,
        CancellationToken cancellationToken) =>
        context.FiscalPeriodAccountLocks.SingleOrDefaultAsync(
            entry => entry.CompanyId == companyId &&
                     entry.FiscalPeriodId == periodId &&
                     entry.ChartOfAccountId == chartOfAccountId,
            cancellationToken);

    private async Task<FiscalPeriod> FindPeriodAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken)
    {
        var period = await context.FiscalPeriods.SingleOrDefaultAsync(
            candidate => candidate.Id == periodId && candidate.CompanyId == companyId,
            cancellationToken);
        return period ?? throw new FinancePostingValidationException("Fiscal period not found in this company.");
    }

    private async Task RequirePostingAccountAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
            candidate => candidate.Id == chartOfAccountId && candidate.CompanyId == companyId,
            cancellationToken);
        if (account is null)
            throw new FinancePostingValidationException("Chart account not found in this company.");
        if (account.IsGroupAccount)
            throw new FinancePostingValidationException(
                $"Chart account '{account.AccountCode}' is a group account and cannot receive postings, so it cannot be locked.");
    }

    private static void RequirePositiveCompany(int companyId)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reason of 1 to 500 characters is required.");
        return trimmed;
    }
}
