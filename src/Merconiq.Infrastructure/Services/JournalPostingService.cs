using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// The finance posting kernel. Validates balanced journal requests against company, account, and
/// period controls, then commits one immutable journal per source action inside a single database
/// transaction. Committed actions replay their result for matching retries and reject conflicting
/// payloads, so no business action can post twice and no half-posted journal can survive.
/// </summary>
public sealed class JournalPostingService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IFiscalCalendarService fiscalCalendar,
    IDocumentNumberService documentNumbers,
    ILogger<JournalPostingService> logger) : IJournalPostingService
{
    /// <summary>Unique source-action index; duplicate submissions classify against this name.</summary>
    public const string SourceActionIndex = "UX_JournalEntries_SourceAction";

    /// <summary>Document-sequence type and prefix used for human-readable journal numbers.</summary>
    public const string JournalDocumentType = "JournalEntry";
    public const string JournalNumberPrefix = "JV";

    /// <inheritdoc />
    public async Task<JournalPostingResult> PostAsync(
        JournalPostingRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var normalized = Normalize(request);
        return await PostCoreAsync(
            normalized, actor, reversalOfJournalEntryId: null, reversalReason: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JournalPostingResult> ReverseAsync(
        ReverseJournalRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reversal reason of 1 to 500 characters is required.");

        JournalPostingResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"finance-period:{request.CompanyId}", cancellationToken);

            var original = await context.JournalEntries
                .Include(entry => entry.Lines)
                .SingleOrDefaultAsync(
                    entry => entry.Id == request.JournalEntryId && entry.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Journal not found in this company.");

            if (original.ReversalOfJournalEntryId is not null)
                throw new FinancePostingConflictException(
                    "A reversal journal cannot be reversed again; post a corrective journal instead.");
            // Fixed-asset journals are replay keys for depreciation months; reversing one would silently
            // stop that month from ever posting again, so asset corrections go through disposal instead.
            if (original.SourceDocumentType == FixedAssetService.SourceDocumentType)
                throw new FinancePostingConflictException(
                    "Fixed-asset postings are corrected through the fixed-asset workflow, not by generic reversal.");

            var reversal = new JournalPostingRequest(
                request.CompanyId,
                request.PostingDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                original.CurrencyCode,
                $"Reversal of {original.JournalNumber}: {reason}",
                JournalDocumentType,
                JournalPostingRules.ReversalSourceDocumentId(original.Id),
                "Reverse",
                1,
                original.Lines.OrderBy(line => line.LineNumber)
                    .Select(line => new JournalLineInput(
                        line.ChartOfAccountId,
                        $"Reversal of {original.JournalNumber}",
                        line.Credit,
                        line.Debit))
                    .ToArray(),
                original.FiscalPeriodId);

            result = await PostCoreAsync(reversal, actor, original.Id, reason, cancellationToken);
        }, cancellationToken: cancellationToken);

        return result;
    }

    /// <inheritdoc />
    public async Task<TrialBalanceResult> GetTrialBalanceAsync(
        int companyId,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        if (fromDate is { } from && toDate is { } to && from > to)
            throw new FinancePostingValidationException(
                "The trial-balance start date must not be after its end date.");

        var lines = context.JournalLines.Where(line => line.CompanyId == companyId);
        if (fromDate is { } fromDay)
            lines = lines.Where(line => line.JournalEntry.PostingDate >= fromDay);
        if (toDate is { } toDay)
            lines = lines.Where(line => line.JournalEntry.PostingDate <= toDay);

        // Aggregated from immutable ledger lines with posted account snapshots; statements are
        // never derived from mutable client-side totals.
        var rows = await lines
            .GroupBy(line => new { line.ChartOfAccountId, line.AccountCodeSnapshot, line.AccountNameSnapshot })
            .Select(group => new TrialBalanceRow(
                group.Key.ChartOfAccountId,
                group.Key.AccountCodeSnapshot,
                group.Key.AccountNameSnapshot,
                group.Sum(line => line.Debit),
                group.Sum(line => line.Credit)))
            .ToListAsync(cancellationToken);

        var ordered = rows.OrderBy(row => row.AccountCode).ThenBy(row => row.ChartOfAccountId).ToList();
        var totalDebit = ordered.Sum(row => row.Debit);
        var totalCredit = ordered.Sum(row => row.Credit);
        if (totalDebit != totalCredit)
        {
            // Every committed journal is balanced, so an unbalanced trial balance can only mean
            // corrupted data; fail loudly instead of presenting numbers that look trustworthy.
            throw new InvalidOperationException(
                $"Trial balance for company {companyId} is unbalanced: debits {totalDebit}, credits {totalCredit}.");
        }

        return new TrialBalanceResult(companyId, fromDate, toDate, ordered, totalDebit, totalCredit);
    }

    /// <inheritdoc />
    public Task<JournalEntry?> GetJournalAsync(
        int companyId,
        int journalEntryId,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.JournalEntries
            .Include(entry => entry.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(
                entry => entry.Id == journalEntryId && entry.CompanyId == companyId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JournalEntry>> ListJournalsAsync(
        int companyId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (fromDate is { } from && toDate is { } to && from > to)
            throw new FinancePostingValidationException(
                "The journal list start date must not be after its end date.");

        var entries = context.JournalEntries.Where(entry => entry.CompanyId == companyId);
        if (fromDate is { } fromDay)
            entries = entries.Where(entry => entry.PostingDate >= fromDay);
        if (toDate is { } toDay)
            entries = entries.Where(entry => entry.PostingDate <= toDay);

        return await entries
            .OrderByDescending(entry => entry.PostingDate)
            .ThenByDescending(entry => entry.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<JournalPostingResult> PostCoreAsync(
        JournalPostingRequest normalized,
        string actor,
        int? reversalOfJournalEntryId,
        string? reversalReason,
        CancellationToken cancellationToken)
    {
        JournalPostingResult result = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync(
                    $"finance-period:{normalized.CompanyId}", cancellationToken);

                var company = await context.Companies.SingleOrDefaultAsync(
                        candidate => candidate.Id == normalized.CompanyId && candidate.IsActive,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Active company not found.");

                var scale = company.CurrencyScale ?? 2;
                JournalPostingRules.Validate(normalized, scale);
                if (!string.Equals(normalized.CurrencyCode, company.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                    throw new FinancePostingValidationException(
                        $"Journals post in the company base currency {company.BaseCurrency}, not {normalized.CurrencyCode}.");

                var requestHash = JournalPostingRules.ComputeRequestHash(normalized);

                var committed = await FindCommittedAsync(normalized, cancellationToken);
                if (committed is not null)
                {
                    if (committed.RequestHash != requestHash)
                        throw new FinancePostingConflictException(
                            "This source action was already posted with a different payload; review the committed journal instead of retrying with changed amounts.");
                    result = ToResult(committed, replayed: true);
                    return;
                }

                // The period gate runs inside this transaction under the same advisory-lock
                // family as period close/reopen, so a close cannot race the validation.
                var period = normalized.FiscalPeriodId is int adjustmentPeriodId
                    ? await fiscalCalendar.EnsureAdjustmentPeriodOpenAsync(
                        normalized.CompanyId, adjustmentPeriodId, normalized.PostingDate, cancellationToken)
                    : await fiscalCalendar.EnsurePeriodOpenForDateAsync(
                        normalized.CompanyId, normalized.PostingDate, cancellationToken);
                await RequireAccountsUnlockedAsync(normalized, period, cancellationToken);

                var entry = await BuildJournalEntryAsync(
                    normalized, company, requestHash, actor, reversalOfJournalEntryId, reversalReason, cancellationToken);
                context.JournalEntries.Add(entry);
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

                result = ToResult(entry, replayed: false);
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await FindCommittedAsync(normalized, cancellationToken) is not null);

        logger.LogInformation(
            "Journal {JournalNumber} {Outcome} for company {CompanyId} source {SourceType}/{SourceDocumentId}/{SourceAction}/{SourceVersion} total {TotalDebit}",
            result.JournalNumber,
            result.Replayed ? "replayed" : "posted",
            normalized.CompanyId,
            normalized.SourceDocumentType,
            normalized.SourceDocumentId,
            normalized.SourceAction,
            normalized.SourceVersion,
            result.TotalDebit);
        return result;
    }

    /// <summary>
    /// Rejects the journal when any touched account is locked for the posting period. Reversals run
    /// through this same gate, so a locked account cannot be corrected inside a locked period either.
    /// </summary>
    private async Task RequireAccountsUnlockedAsync(
        JournalPostingRequest request,
        FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        var accountIds = request.Lines.Select(line => line.ChartOfAccountId).Distinct().ToList();
        var lockedAccountIds = await context.FiscalPeriodAccountLocks
            .Where(entry => entry.CompanyId == request.CompanyId &&
                            entry.FiscalPeriodId == period.Id &&
                            entry.IsLocked &&
                            accountIds.Contains(entry.ChartOfAccountId))
            .Select(entry => entry.ChartOfAccountId)
            .ToListAsync(cancellationToken);
        if (lockedAccountIds.Count == 0)
            return;

        var lockedCodes = await context.ChartOfAccounts
            .Where(account => account.CompanyId == request.CompanyId && lockedAccountIds.Contains(account.Id))
            .OrderBy(account => account.AccountCode)
            .Select(account => account.AccountCode)
            .ToListAsync(cancellationToken);
        throw new FinancePostingValidationException(
            $"Chart account(s) {string.Join(", ", lockedCodes)} are locked for fiscal period '{period.Name}' and cannot receive postings.");
    }

    private async Task<JournalEntry> BuildJournalEntryAsync(
        JournalPostingRequest request,
        Company company,
        string requestHash,
        string actor,
        int? reversalOfJournalEntryId,
        string? reversalReason,
        CancellationToken cancellationToken)
    {
        var accountIds = request.Lines.Select(line => line.ChartOfAccountId).Distinct().ToList();
        var accounts = await context.ChartOfAccounts
            .Where(account => account.CompanyId == request.CompanyId && accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, cancellationToken);

        foreach (var accountId in accountIds)
        {
            if (!accounts.TryGetValue(accountId, out var account))
                throw new FinancePostingValidationException(
                    $"Chart account {accountId} does not exist in this company.");
            if (!account.IsActive)
                throw new FinancePostingValidationException(
                    $"Chart account '{account.AccountCode}' is inactive and cannot receive postings.");
            if (account.IsGroupAccount)
                throw new FinancePostingValidationException(
                    $"Chart account '{account.AccountCode}' is a group account and cannot receive postings.");
        }

        var totalDebit = request.Lines.Sum(line => line.Debit);
        var journalNumber = await documentNumbers.AllocateAsync(
            request.CompanyId, JournalDocumentType, request.PostingDate.Year, JournalNumberPrefix, cancellationToken);

        var entry = new JournalEntry
        {
            CompanyId = request.CompanyId,
            JournalNumber = journalNumber,
            PostingDate = request.PostingDate,
            CurrencyCode = company.BaseCurrency.ToUpperInvariant(),
            Description = request.Description,
            SourceDocumentType = request.SourceDocumentType,
            SourceDocumentId = request.SourceDocumentId,
            SourceAction = request.SourceAction,
            SourceVersion = request.SourceVersion,
            RequestHash = requestHash,
            TotalDebit = totalDebit,
            TotalCredit = totalDebit,
            ReversalOfJournalEntryId = reversalOfJournalEntryId,
            ReversalReason = reversalReason,
            FiscalPeriodId = request.FiscalPeriodId,
            PostedAtUtc = DateTime.UtcNow,
            PostedBy = actor.Trim(),
            Lines = request.Lines
                .Select((line, index) => new JournalLine
                {
                    CompanyId = request.CompanyId,
                    LineNumber = index + 1,
                    ChartOfAccountId = line.ChartOfAccountId,
                    AccountCodeSnapshot = accounts[line.ChartOfAccountId].AccountCode,
                    AccountNameSnapshot = accounts[line.ChartOfAccountId].Name,
                    Description = line.Description,
                    Debit = line.Debit,
                    Credit = line.Credit
                })
                .ToList()
        };
        return entry;
    }

    private Task<JournalEntry?> FindCommittedAsync(
        JournalPostingRequest request,
        CancellationToken cancellationToken) =>
        context.JournalEntries.SingleOrDefaultAsync(entry =>
                entry.CompanyId == request.CompanyId &&
                entry.SourceDocumentType == request.SourceDocumentType &&
                entry.SourceDocumentId == request.SourceDocumentId &&
                entry.SourceAction == request.SourceAction &&
                entry.SourceVersion == request.SourceVersion,
            cancellationToken);

    private static JournalPostingRequest Normalize(JournalPostingRequest request)
    {
        var lines = request.Lines
            .Select(line => line with
            {
                Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim()
            })
            .ToArray();
        return request with
        {
            CurrencyCode = request.CurrencyCode?.Trim().ToUpperInvariant() ?? string.Empty,
            Description = request.Description?.Trim() ?? string.Empty,
            SourceDocumentType = request.SourceDocumentType?.Trim() ?? string.Empty,
            SourceDocumentId = request.SourceDocumentId?.Trim() ?? string.Empty,
            SourceAction = request.SourceAction?.Trim() ?? string.Empty,
            Lines = lines
        };
    }

    private static JournalPostingResult ToResult(JournalEntry entry, bool replayed) =>
        new(entry.Id, entry.JournalNumber, entry.PostingDate, entry.TotalDebit, entry.TotalCredit, replayed);
}
