using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Imports bank statements and reconciles them against the bank account's ledger lines. Matching is a
/// reviewed, one-to-one decision; this service never posts to the ledger, so reconciling a statement
/// cannot change any financial total.
/// </summary>
public interface IBankReconciliationService
{
    /// <summary>Imports one statement for a company bank account; the statement must foot exactly.</summary>
    Task<BankStatement> ImportStatementAsync(
        BankStatementImportRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the company's imported statements, newest statement date first.</summary>
    Task<IReadOnlyList<BankStatement>> ListStatementsAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one statement with its active matches, or <see langword="null"/> outside the company.</summary>
    Task<BankStatementView?> GetStatementAsync(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms that one bank line and one ledger line on the same account are the same movement.</summary>
    Task<BankStatementMatch> MatchLineAsync(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        int journalLineId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws an active match with a mandatory reason; the match row stays as history.</summary>
    Task<BankStatementMatch> UnmatchLineAsync(
        int companyId,
        int bankStatementId,
        int bankStatementLineId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles one statement against the bank account's ledger as of the statement date.</summary>
    Task<BankReconciliationReport> GetReconciliationAsync(
        int companyId,
        int bankStatementId,
        CancellationToken cancellationToken = default);
}
