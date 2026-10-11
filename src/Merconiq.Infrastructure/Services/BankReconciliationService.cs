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
/// Bank statement import, reviewed matching and reconciliation. Every write runs under one company-scoped
/// advisory lock, and the database keeps at most one active match per statement line and per ledger line,
/// so concurrent reviewers cannot double-match a movement. Reads never change ledger or stock state.
/// </summary>
public sealed class BankReconciliationService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<BankReconciliationService> logger) : IBankReconciliationService
{
    /// <inheritdoc />
    public async Task<BankStatement> ImportStatementAsync(
        BankStatementImportRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(request.CompanyId);
        if (request.ChartOfAccountId <= 0)
            throw new FinancePostingValidationException("A positive ChartOfAccountId is required.");

        BankStatement created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync(
                $"bank-reconciliation:{request.CompanyId}", cancellationToken);

            var company = await context.Companies.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.CompanyId && candidate.IsActive,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Active company not found.");
            BankReconciliationRules.ValidateStatement(
                request.OpeningBalance, request.ClosingBalance, request.Lines, company.CurrencyScale ?? 2);

            var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ChartOfAccountId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Chart account not found in this company.");
            if (account.IsGroupAccount)
                throw new FinancePostingValidationException(
                    $"Chart account '{account.AccountCode}' is a group account and cannot carry bank movements.");
            if (!account.IsActive)
                throw new FinancePostingValidationException(
                    $"Chart account '{account.AccountCode}' is inactive and cannot be reconciled.");

            var lineAfterStatementDate = request.Lines.FirstOrDefault(line => line.ValueDate > request.StatementDate);
            if (lineAfterStatementDate is not null)
                throw new FinancePostingValidationException(
                    $"A statement line is valued {lineAfterStatementDate.ValueDate:yyyy-MM-dd}, after the statement date {request.StatementDate:yyyy-MM-dd}.");

            var duplicate = await context.BankStatements.AnyAsync(
                statement => statement.CompanyId == request.CompanyId &&
                             statement.ChartOfAccountId == request.ChartOfAccountId &&
                             statement.StatementDate == request.StatementDate,
                cancellationToken);
            if (duplicate)
                throw new FinancePostingConflictException(
                    $"A statement for this account is already imported for {request.StatementDate:yyyy-MM-dd}.");

            var statement = new BankStatement
            {
                CompanyId = request.CompanyId,
                ChartOfAccountId = request.ChartOfAccountId,
                StatementDate = request.StatementDate,
                OpeningBalance = request.OpeningBalance,
                ClosingBalance = request.ClosingBalance,
                Reference = TrimOrNull(request.Reference),
                ImportedBy = actor.Trim(),
                ImportedAtUtc = DateTime.UtcNow,
                Lines = request.Lines
                    .Select((line, index) => new BankStatementLine
                    {
                        CompanyId = request.CompanyId,
                        LineNumber = index + 1,
                        ValueDate = line.ValueDate,
                        Description = line.Description.Trim(),
                        Reference = TrimOrNull(line.Reference),
                        Amount = line.Amount
                    })
                    .ToList()
            };
            context.BankStatements.Add(statement);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = statement;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Imported bank statement {BankStatementId} for account {ChartOfAccountId} of company {CompanyId} with {LineCount} lines",
            created.Id, request.ChartOfAccountId, request.CompanyId, created.Lines.Count);
        return created;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankStatement>> ListStatementsAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.BankStatements
            .Where(statement => statement.CompanyId == companyId)
            .OrderByDescending(statement => statement.StatementDate)
            .ThenByDescending(statement => statement.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BankStatementView?> GetStatementAsync(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var statement = await context.BankStatements
            .Include(candidate => candidate.Lines.OrderBy(line => line.LineNumber))
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bankStatementId && candidate.CompanyId == companyId,
                cancellationToken);
        if (statement is null)
            return null;

        var lineIds = statement.Lines.Select(line => line.Id).ToList();
        var matches = await context.BankStatementMatches
            .Where(match => match.CompanyId == companyId && match.IsActive && lineIds.Contains(match.BankStatementLineId))
            .ToDictionaryAsync(match => match.BankStatementLineId, match => match.JournalLineId, cancellationToken);
        return new BankStatementView(statement, matches);
    }

    /// <inheritdoc />
    public async Task<BankStatementMatch> MatchLineAsync(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        int journalLineId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        BankStatementMatch created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"bank-reconciliation:{companyId}", cancellationToken);

            var statement = await FindStatementAsync(companyId, bankStatementId, cancellationToken);
            var line = await FindLineAsync(companyId, statement.Id, bankStatementLineId, cancellationToken);
            if (await HasActiveLineMatchAsync(companyId, line.Id, cancellationToken))
                throw new FinancePostingConflictException(
                    "This statement line is already matched; unmatch it before matching it again.");

            var journalLine = await context.JournalLines
                .Include(candidate => candidate.JournalEntry)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == journalLineId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Ledger line not found in this company.");
            if (journalLine.ChartOfAccountId != statement.ChartOfAccountId)
                throw new FinancePostingValidationException(
                    "The ledger line belongs to a different account than this bank statement.");
            if (journalLine.JournalEntry.PostingDate > statement.StatementDate)
                throw new FinancePostingValidationException(
                    $"The ledger line is posted on {journalLine.JournalEntry.PostingDate:yyyy-MM-dd}, after the statement date {statement.StatementDate:yyyy-MM-dd}.");

            var ledgerAmount = journalLine.Debit - journalLine.Credit;
            if (ledgerAmount != line.Amount)
                throw new FinancePostingValidationException(
                    $"The ledger movement {ledgerAmount} does not equal the statement line amount {line.Amount}.");

            var alreadyMatched = await context.BankStatementMatches.AnyAsync(
                match => match.CompanyId == companyId && match.IsActive && match.JournalLineId == journalLine.Id,
                cancellationToken);
            if (alreadyMatched)
                throw new FinancePostingConflictException(
                    "The ledger line is already matched to another statement line.");

            var match = new BankStatementMatch
            {
                CompanyId = companyId,
                BankStatementLineId = line.Id,
                JournalLineId = journalLine.Id,
                IsActive = true,
                MatchedBy = actor.Trim(),
                MatchedAtUtc = DateTime.UtcNow
            };
            context.BankStatementMatches.Add(match);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = match;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Matched statement line {BankStatementLineId} to ledger line {JournalLineId} for company {CompanyId}",
            bankStatementLineId, journalLineId, companyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<BankStatementMatch> UnmatchLineAsync(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var normalizedReason = RequireReason(reason);

        BankStatementMatch withdrawn = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"bank-reconciliation:{companyId}", cancellationToken);

            var statement = await FindStatementAsync(companyId, bankStatementId, cancellationToken);
            var line = await FindLineAsync(companyId, statement.Id, bankStatementLineId, cancellationToken);
            var match = await context.BankStatementMatches.SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId &&
                             candidate.IsActive &&
                             candidate.BankStatementLineId == line.Id,
                cancellationToken)
                ?? throw new FinancePostingConflictException("This statement line is not matched.");

            match.IsActive = false;
            match.UnmatchedBy = actor.Trim();
            match.UnmatchedAtUtc = DateTime.UtcNow;
            match.UnmatchReason = normalizedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            withdrawn = match;
        }, cancellationToken: cancellationToken);

        logger.LogWarning(
            "Withdrew match of statement line {BankStatementLineId} for company {CompanyId}; reason recorded",
            bankStatementLineId, companyId);
        return withdrawn;
    }

    /// <inheritdoc />
    public async Task<BankReconciliationReport> GetReconciliationAsync(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var statement = await FindStatementAsync(companyId, bankStatementId, cancellationToken);

        var lineIds = await context.BankStatementLines
            .Where(line => line.CompanyId == companyId && line.BankStatementId == statement.Id)
            .Select(line => line.Id)
            .ToListAsync(cancellationToken);
        var matchedLineIds = (await context.BankStatementMatches
                .Where(match => match.CompanyId == companyId && match.IsActive && lineIds.Contains(match.BankStatementLineId))
                .Select(match => match.BankStatementLineId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var unmatchedLineAmounts = await context.BankStatementLines
            .Where(line => line.CompanyId == companyId &&
                           line.BankStatementId == statement.Id &&
                           !matchedLineIds.Contains(line.Id))
            .Select(line => line.Amount)
            .ToListAsync(cancellationToken);
        var unmatchedStatementAmount = unmatchedLineAmounts.Sum();

        var ledger = context.JournalLines.Where(line =>
            line.CompanyId == companyId &&
            line.ChartOfAccountId == statement.ChartOfAccountId &&
            line.JournalEntry.PostingDate <= statement.StatementDate);
        var ledgerBalance = await ledger.SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken) ?? 0m;

        var matchedJournalIds = context.BankStatementMatches
            .Where(match => match.CompanyId == companyId && match.IsActive)
            .Select(match => match.JournalLineId);
        var unmatchedLedger = ledger.Where(line => !matchedJournalIds.Contains(line.Id));
        var unmatchedLedgerAmount = await unmatchedLedger
            .SumAsync(line => (decimal?)(line.Debit - line.Credit), cancellationToken) ?? 0m;
        var unmatchedLedgerCount = await unmatchedLedger.CountAsync(cancellationToken);
        var unmatchedItems = await unmatchedLedger
            .OrderBy(line => line.JournalEntry.PostingDate)
            .ThenBy(line => line.Id)
            .Take(BankReconciliationRules.MaximumListedLedgerItems)
            .Select(line => new UnmatchedLedgerItem(
                line.Id,
                line.JournalEntryId,
                line.JournalEntry.PostingDate,
                line.Debit - line.Credit,
                line.Description ?? line.JournalEntry.Description))
            .ToListAsync(cancellationToken);

        var difference = BankReconciliationRules.Difference(
            statement.ClosingBalance, ledgerBalance, unmatchedLedgerAmount, unmatchedStatementAmount);
        var unmatchedStatementLineCount = unmatchedLineAmounts.Count;
        return new BankReconciliationReport(
            companyId,
            statement.Id,
            statement.ChartOfAccountId,
            statement.StatementDate,
            statement.ClosingBalance,
            ledgerBalance,
            unmatchedLedgerAmount,
            unmatchedStatementAmount,
            statement.ClosingBalance + unmatchedLedgerAmount,
            ledgerBalance + unmatchedStatementAmount,
            difference,
            lineIds.Count - unmatchedStatementLineCount,
            unmatchedStatementLineCount,
            unmatchedLedgerCount,
            BankReconciliationRules.IsReconciled(difference, unmatchedStatementLineCount),
            unmatchedItems);
    }

    private async Task<BankStatement> FindStatementAsync(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken) =>
        await context.BankStatements.SingleOrDefaultAsync(
            candidate => candidate.Id == bankStatementId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Bank statement not found in this company.");

    private async Task<BankStatementLine> FindLineAsync(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        CancellationToken cancellationToken) =>
        await context.BankStatementLines.SingleOrDefaultAsync(
            candidate => candidate.Id == bankStatementLineId &&
                         candidate.BankStatementId == bankStatementId &&
                         candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Statement line not found in this bank statement.");

    private Task<bool> HasActiveLineMatchAsync(
        int companyId,
        int bankStatementLineId,
        CancellationToken cancellationToken) =>
        context.BankStatementMatches.AnyAsync(
            match => match.CompanyId == companyId &&
                     match.IsActive &&
                     match.BankStatementLineId == bankStatementLineId,
            cancellationToken);

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

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
