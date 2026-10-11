using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IIntercompanyService
{
    Task<IntercompanyRelationshipView> ConfigureRelationshipAsync(
        ConfigureIntercompanyRelationshipRequest request,
        CancellationToken cancellationToken = default);

    Task<IntercompanyTransactionView> PostTransactionAsync(
        PostIntercompanyTransactionRequest request,
        CancellationToken cancellationToken = default);

    Task<IntercompanyEliminationReport> GetEliminationReportAsync(
        int parentCompanyId,
        int subsidiaryCompanyId,
        CancellationToken cancellationToken = default);
}
