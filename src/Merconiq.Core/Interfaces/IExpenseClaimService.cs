using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Employee expense claims: draft, submit, approve or reject, and pay. Approval posts the claim to the
/// employee payable, and payment settles that payable from a bank account, both through the finance kernel.
/// </summary>
public interface IExpenseClaimService
{
    /// <summary>Creates a draft claim with a company-allocated number.</summary>
    Task<ExpenseClaim> CreateAsync(CreateExpenseClaimRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Lists the company's claims, newest first.</summary>
    Task<IReadOnlyList<ExpenseClaim>> ListAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Gets one claim with its lines, or <see langword="null"/> outside the company.</summary>
    Task<ExpenseClaimView?> GetAsync(int companyId, int claimId, CancellationToken cancellationToken = default);

    /// <summary>Moves a draft claim to submitted for review.</summary>
    Task<ExpenseClaim> SubmitAsync(int companyId, int claimId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Approves a submitted claim and posts its expenses against the employee payable.</summary>
    Task<ExpenseClaim> ApproveAsync(int companyId, int claimId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Rejects a submitted claim with a mandatory reason; a rejected claim is final.</summary>
    Task<ExpenseClaim> RejectAsync(int companyId, int claimId, string reason, string actor, CancellationToken cancellationToken = default);

    /// <summary>Pays an approved claim from a bank account and settles the employee payable.</summary>
    Task<ExpenseClaim> PayAsync(
        int companyId,
        int claimId,
        int paymentChartOfAccountId,
        DateOnly paymentDate,
        string actor,
        CancellationToken cancellationToken = default);
}
