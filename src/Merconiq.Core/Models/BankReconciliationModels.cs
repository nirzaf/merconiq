using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One movement as supplied by the bank; the amount is signed from the bank account's view.</summary>
public sealed record BankStatementLineInput(
    DateOnly ValueDate,
    string Description,
    string? Reference,
    decimal Amount);

/// <summary>Imports one bank statement for a company bank account; ownership comes from the route and session.</summary>
public sealed record BankStatementImportRequest(
    int CompanyId,
    int ChartOfAccountId,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    string? Reference,
    IReadOnlyList<BankStatementLineInput> Lines);

/// <summary>A bank-account ledger line that no statement line explains yet, listed for review.</summary>
public sealed record UnmatchedLedgerItem(
    int JournalLineId,
    int JournalEntryId,
    DateOnly PostingDate,
    decimal Amount,
    string Description);

/// <summary>
/// Bank reconciliation as of a statement date. Adjusted bank balance is the closing balance plus
/// unmatched ledger movements; adjusted book balance is the ledger balance plus unmatched bank
/// movements. The statement reconciles when those agree and every bank line is explained.
/// </summary>
public sealed record BankReconciliationReport(
    int CompanyId,
    int BankStatementId,
    int ChartOfAccountId,
    DateOnly StatementDate,
    decimal ClosingBalance,
    decimal LedgerBalance,
    decimal UnmatchedLedgerAmount,
    decimal UnmatchedStatementAmount,
    decimal AdjustedBankBalance,
    decimal AdjustedBookBalance,
    decimal Difference,
    int MatchedStatementLineCount,
    int UnmatchedStatementLineCount,
    int UnmatchedLedgerLineCount,
    bool IsReconciled,
    IReadOnlyList<UnmatchedLedgerItem> UnmatchedLedgerItems);

/// <summary>A statement with its lines, and the journal line each active match points to, keyed by statement line id.</summary>
public sealed record BankStatementView(
    BankStatement Statement,
    IReadOnlyDictionary<int, int> ActiveMatchesByStatementLineId);
