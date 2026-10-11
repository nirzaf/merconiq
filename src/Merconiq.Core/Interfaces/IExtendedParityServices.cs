using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface ISerialNumberService
{
    Task<IReadOnlyList<SerialNumber>> RegisterSerialsAsync(RegisterSerialRequest request, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SerialNumber>> DeliverSerialsAsync(DeliverSerialRequest request, string actor, CancellationToken cancellationToken = default);
    Task<SerialHistoryResult?> GetSerialHistoryAsync(int companyId, int itemId, string serialNo, CancellationToken cancellationToken = default);
}

public interface IFifoValuationService
{
    Task<StockFifoLayer> RecordInwardLayerAsync(int companyId, int itemId, int locationId, int quantity, decimal unitCost, string? sourceReference, string actor, CancellationToken cancellationToken = default);
    Task<FifoConsumptionResult> ConsumeFifoLayersAsync(int companyId, int itemId, int locationId, int quantity, string actor, CancellationToken cancellationToken = default);
}

public interface IAutoReorderService
{
    Task<ItemReorderRule> SetReorderRuleAsync(SetReorderRuleRequest request, string actor, CancellationToken cancellationToken = default);
    Task<AutoReorderTriggerSummary> EvaluateReorderTriggersAsync(int companyId, string actor, CancellationToken cancellationToken = default);
}

public interface IShopFloorService
{
    Task<Workstation> CreateWorkstationAsync(CreateWorkstationRequest request, string actor, CancellationToken cancellationToken = default);
    Task<RoutingOperation> CreateRoutingOperationAsync(CreateRoutingOperationRequest request, string actor, CancellationToken cancellationToken = default);
    Task<JobCard> CreateJobCardAsync(CreateJobCardRequest request, string actor, CancellationToken cancellationToken = default);
    Task<JobCard> StartJobCardAsync(int jobCardId, string actor, CancellationToken cancellationToken = default);
    Task<JobCard> CompleteJobCardAsync(CompleteJobCardRequest request, string actor, CancellationToken cancellationToken = default);
}

public interface IBankingFeedService
{
    Task<BankStatementFile> ImportStatementAsync(ImportBankStatementRequest request, string actor, CancellationToken cancellationToken = default);
    Task<CashFlowStatementReport> GenerateCashFlowStatementAsync(int companyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}

public interface ICommercialLifecycleService
{
    Task<SubcontractingOrder> CreateSubcontractingOrderAsync(CreateSubcontractingOrderRequest request, string actor, CancellationToken cancellationToken = default);
    Task<SalesPartnerCommission> AccrueCommissionAsync(CalculateCommissionRequest request, string actor, CancellationToken cancellationToken = default);
    Task<DunningNotice> IssueDunningNoticeAsync(IssueDunningNoticeRequest request, string actor, CancellationToken cancellationToken = default);
}
