using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Posts immutable, balanced general-ledger journals exactly once per source action and
/// produces ledger-backed trial-balance reads. All effects of one call commit atomically.
/// </summary>
public interface IJournalPostingService
{
    /// <summary>
    /// Validates and commits one balanced journal. A committed source action with the same
    /// payload replays its committed result; a conflicting payload is rejected.
    /// </summary>
    Task<JournalPostingResult> PostAsync(
        JournalPostingRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts one source-linked reversal of a posted journal with swapped debit/credit lines.
    /// Reversals are not reversible and repeated requests replay the first reversal.
    /// </summary>
    Task<JournalPostingResult> ReverseAsync(
        ReverseJournalRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a ledger-backed trial balance for the company and optional date window.</summary>
    Task<TrialBalanceResult> GetTrialBalanceAsync(
        int companyId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one company-owned journal with its lines, or <see langword="null"/>.</summary>
    Task<JournalEntry?> GetJournalAsync(
        int companyId,
        int journalEntryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists up to <paramref name="limit"/> posted journals newest-first inside the optional
    /// posting-date window. Callers pass one extra row to detect a following page.
    /// </summary>
    Task<IReadOnlyList<JournalEntry>> ListJournalsAsync(
        int companyId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
