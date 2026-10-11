using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Resolves named posting roles to active, posting-capable company chart accounts. Posting
/// adapters call this instead of hard-coded account identifiers or untrusted user-supplied
/// control accounts.
/// </summary>
public interface IAccountPostingPolicy
{
    Task<ResolvedPostingAccount> ResolveAsync(
        int companyId,
        AccountPostingRole role,
        CancellationToken cancellationToken = default);
}
