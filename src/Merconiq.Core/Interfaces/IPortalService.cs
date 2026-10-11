using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IPortalService
{
    Task<CustomerPortalOverview> GetCustomerPortalOverviewAsync(
        int companyId,
        int customerId,
        string contactEmail,
        CancellationToken cancellationToken = default);

    Task<SupplierPortalOverview> GetSupplierPortalOverviewAsync(
        int companyId,
        int supplierId,
        string contactEmail,
        CancellationToken cancellationToken = default);

    Task<SupplierCollaborationSummary> SubmitSupplierAcknowledgmentAsync(
        SubmitSupplierAcknowledgmentRequest request,
        string contactEmail,
        CancellationToken cancellationToken = default);

    Task<SupplierCollaborationSummary> SubmitSupplierInvoiceReferenceAsync(
        SubmitSupplierInvoiceReferenceRequest request,
        string contactEmail,
        CancellationToken cancellationToken = default);

    Task<SupplierCollaborationSummary> ReviewSupplierSubmissionAsync(
        ReviewSupplierSubmissionRequest request,
        CancellationToken cancellationToken = default);
}
