using Merconiq.Core.Entities;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Manages per-period posting locks on individual chart accounts. Locks and the posting kernel share
/// one company-scoped advisory-lock family, so a lock cannot commit while a journal is validating
/// the same period.
/// </summary>
public interface IFiscalAccountLockService
{
    /// <summary>Locks one posting account inside one fiscal period; rejects a second active lock.</summary>
    Task<FiscalPeriodAccountLock> LockAccountAsync(
        int companyId,
        int periodId,
        int chartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Releases an active lock with a mandatory reason; rejects an account that is not locked.</summary>
    Task<FiscalPeriodAccountLock> ReleaseAccountAsync(
        int companyId,
        int periodId,
        int chartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the lock rows recorded for one company's fiscal period.</summary>
    Task<IReadOnlyList<FiscalPeriodAccountLock>> ListLocksAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default);
}
