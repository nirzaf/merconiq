using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Resolves named posting roles to company chart accounts through active company mappings.
/// A resolved account is always company-owned, active, and a posting (non-group) account, so
/// adapters can never post to another company's books or to a summarization-only account.
/// </summary>
public sealed class AccountPostingPolicyService(
    InventoryDbContext context) : IAccountPostingPolicy
{
    /// <inheritdoc />
    public async Task<ResolvedPostingAccount> ResolveAsync(
        int companyId,
        AccountPostingRole role,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");

        var mapping = await context.CompanyAccountMappings.SingleOrDefaultAsync(
                candidate => candidate.CompanyId == companyId && candidate.Role == role && candidate.IsActive,
                cancellationToken)
            ?? throw new FinancePostingValidationException(
                $"No active account mapping for role '{role}' in this company; configure the posting policy first.");

        var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
                candidate => candidate.Id == mapping.ChartOfAccountId && candidate.CompanyId == companyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException(
                $"The account mapped to role '{role}' does not exist in this company.");

        if (!account.IsActive)
            throw new FinancePostingValidationException(
                $"The account mapped to role '{role}' ('{account.AccountCode}') is inactive.");
        if (account.IsGroupAccount)
            throw new FinancePostingValidationException(
                $"The account mapped to role '{role}' ('{account.AccountCode}') is a group account.");

        return new ResolvedPostingAccount(account.Id, account.AccountCode, account.Name);
    }
}
