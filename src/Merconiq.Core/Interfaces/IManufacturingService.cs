using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Manufacturing: versioned BOMs and work orders whose completion consumes components through
/// the stock ledger at moving-average cost, receives finished goods at the exact consumed unit
/// cost, and posts the paired WIP journals through the finance kernel — atomically, once.
/// </summary>
public interface IManufacturingService
{
    Task<BillOfMaterials> CreateBillOfMaterialsAsync(
        CreateBomRequest request, string actor, CancellationToken cancellationToken = default);

    Task<WorkOrder> CreateWorkOrderAsync(
        CreateWorkOrderRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Releases a draft work order after validating the BOM and stock-relevant data.</summary>
    Task<WorkOrder> ReleaseWorkOrderAsync(
        int companyId, int workOrderId, string actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a released work order: consumes components, receives finished goods, and posts
    /// the paired WIP journals exactly once; repeated calls replay the completed order.
    /// </summary>
    Task<WorkOrder> CompleteWorkOrderAsync(
        int companyId, int workOrderId, StockMutationScope mutationScope, string actor,
        CancellationToken cancellationToken = default);

    Task<WorkOrder?> GetWorkOrderAsync(
        int companyId, int workOrderId, CancellationToken cancellationToken = default);

    /// <summary>Advisory material availability proposal for one work order; commits nothing.</summary>
    Task<MaterialProposal> GetMaterialProposalAsync(
        int companyId, int workOrderId, CancellationToken cancellationToken = default);
}
