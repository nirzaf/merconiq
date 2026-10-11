using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Builds financial statements from the immutable ledger. Every included account must carry a
/// statement classification; statements are read-only projections and never mutate anything.
/// </summary>
public interface IFinanceStatementService
{
    /// <summary>Income minus expenses for the posting-date window, from posted ledger lines.</summary>
    Task<ProfitAndLossStatement> GetProfitAndLossAsync(
        int companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assets, liabilities and equity as of a date, plus retained profit; the statement must
    /// balance (assets = liabilities + equity + profit) and fails loudly otherwise.
    /// </summary>
    Task<BalanceSheetStatement> GetBalanceSheetAsync(
        int companyId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);
}
