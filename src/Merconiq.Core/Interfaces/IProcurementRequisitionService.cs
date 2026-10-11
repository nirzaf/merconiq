using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IProcurementRequisitionService
{
    Task<MaterialRequestResult> CreateMaterialRequestAsync(CreateMaterialRequest request, string actor, CancellationToken cancellationToken = default);
    Task<MaterialRequest> ApproveMaterialRequestAsync(int companyId, int materialRequestId, string actor, CancellationToken cancellationToken = default);
    Task<SupplierQuotation> CreateSupplierQuotationAsync(CreateSupplierQuotationRequest request, string actor, CancellationToken cancellationToken = default);
    Task<SupplierQuotationComparisonReport> CompareSupplierQuotationsAsync(int companyId, int itemId, CancellationToken cancellationToken = default);
}
