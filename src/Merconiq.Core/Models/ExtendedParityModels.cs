using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

// Serial Numbers
public sealed record RegisterSerialRequest(
    int CompanyId,
    int ItemId,
    int LocationId,
    IReadOnlyList<string> SerialNumbers,
    int? GoodsReceiptLineId = null,
    int? WarrantyMonths = 12);

public sealed record DeliverSerialRequest(
    int CompanyId,
    int ItemId,
    int LocationId,
    int CustomerId,
    IReadOnlyList<string> SerialNumbers,
    int? DeliveryNoteLineId = null);

public sealed record SerialHistoryResult(
    string SerialNo,
    int ItemId,
    SerialNumberStatus Status,
    int? LocationId,
    DateOnly? WarrantyExpiryDate,
    int? CustomerId);

// FIFO Valuation
public sealed record ConsumedLayerDetail(
    int LayerId,
    int ConsumedQuantity,
    decimal UnitCost,
    decimal LayerTotal);

public sealed record FifoConsumptionResult(
    decimal TotalCost,
    decimal AverageUnitCost,
    IReadOnlyList<ConsumedLayerDetail> Layers);

// Auto-Reorder
public sealed record SetReorderRuleRequest(
    int CompanyId,
    int ItemId,
    int LocationId,
    int ReorderLevel,
    int ReorderQuantity);

public sealed record AutoReorderTriggerSummary(
    int EvaluatedCount,
    int TriggeredCount,
    IReadOnlyList<MaterialRequestResult> CreatedRequisitions);

// Workstations & Job Cards
public sealed record CreateWorkstationRequest(
    int CompanyId,
    string Name,
    decimal HourlyRate,
    decimal CapacityHours = 8m);

public sealed record CreateRoutingOperationRequest(
    int CompanyId,
    string OperationName,
    int WorkstationId,
    int StandardDurationMinutes,
    decimal CostPerHour);

public sealed record CreateJobCardRequest(
    int CompanyId,
    int WorkOrderId,
    int RoutingOperationId,
    string Technician);

public sealed record CompleteJobCardRequest(
    int JobCardId,
    int ActualMinutes);

// Banking & Cash Flow
public sealed record ImportBankStatementLine(
    DateOnly TransactionDate,
    string Reference,
    string Payee,
    decimal Amount);

public sealed record ImportBankStatementRequest(
    int CompanyId,
    int BankAccountId,
    string FileName,
    BankStatementFormat Format,
    DateOnly StatementDate,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyList<ImportBankStatementLine> Lines);

public sealed record CashFlowStatementReport(
    int CompanyId,
    DateOnly FromDate,
    DateOnly ToDate,
    decimal NetIncome,
    decimal OperatingCashFlow,
    decimal InvestingCashFlow,
    decimal FinancingCashFlow,
    decimal NetCashChange);

// Subcontracting, Commissions, Dunning
public sealed record CreateSubcontractingOrderRequest(
    int CompanyId,
    int SupplierId,
    int FinishedItemId,
    int Quantity,
    decimal ServiceRate,
    IReadOnlyList<(int ComponentItemId, decimal RequiredQuantity)> Components);

public sealed record CalculateCommissionRequest(
    int CompanyId,
    string PartnerName,
    int SalesInvoiceId,
    decimal CommissionPercentage);

public sealed record IssueDunningNoticeRequest(
    int CompanyId,
    int SalesInvoiceId,
    DunningLevel Level,
    decimal LateFee,
    decimal Interest);
