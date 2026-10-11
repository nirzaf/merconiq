using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One requested general-ledger line. Exactly one side must be positive.</summary>
public sealed record JournalLineInput(
    int ChartOfAccountId,
    string? Description,
    decimal Debit,
    decimal Credit);

/// <summary>
/// One posting action to commit into the general ledger. The company/source tuple is the
/// idempotency key: committed actions replay their result and reject conflicting payloads.
/// </summary>
public sealed record JournalPostingRequest(
    int CompanyId,
    DateOnly PostingDate,
    string CurrencyCode,
    string Description,
    string SourceDocumentType,
    string SourceDocumentId,
    string SourceAction,
    int SourceVersion,
    IReadOnlyList<JournalLineInput> Lines);

/// <summary>The committed-or-replayed outcome of one posting action.</summary>
public sealed record JournalPostingResult(
    int JournalEntryId,
    string JournalNumber,
    DateOnly PostingDate,
    decimal TotalDebit,
    decimal TotalCredit,
    bool Replayed);

/// <summary>Requests a source-linked reversal of one posted journal; reversals are not reversible.</summary>
public sealed record ReverseJournalRequest(
    int CompanyId,
    int JournalEntryId,
    string Reason,
    DateOnly? PostingDate = null);

/// <summary>One account's ledger movement inside a trial-balance window.</summary>
public sealed record TrialBalanceRow(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit);

/// <summary>Ledger-backed trial balance; debits always equal credits because only balanced journals exist.</summary>
public sealed record TrialBalanceResult(
    int CompanyId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    IReadOnlyList<TrialBalanceRow> Rows,
    decimal TotalDebit,
    decimal TotalCredit);

/// <summary>Creates one fiscal year and its monthly periods.</summary>
public sealed record CreateFiscalYearRequest(
    int CompanyId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>Upserts one company account-role mapping.</summary>
public sealed record CompanyAccountMappingInput(
    int CompanyId,
    AccountPostingRole Role,
    int ChartOfAccountId,
    bool IsActive);

/// <summary>A posting-capable chart account resolved through the company posting policy.</summary>
public sealed record ResolvedPostingAccount(
    int ChartOfAccountId,
    string AccountCode,
    string AccountName);
