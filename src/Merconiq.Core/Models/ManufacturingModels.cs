using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>One component line of a new BOM.</summary>
public sealed record BomLineInput(int ComponentItemId, decimal QuantityPerUnit);

/// <summary>Creates one BOM version for a finished item; idempotent per (company, item, version).</summary>
public sealed record CreateBomRequest(
    int CompanyId,
    int ItemId,
    int Version,
    IReadOnlyList<BomLineInput> Lines);

/// <summary>Creates one work order against a BOM.</summary>
public sealed record CreateWorkOrderRequest(
    int CompanyId,
    int BillOfMaterialsId,
    int Quantity,
    int LocationId,
    string? Notes = null);

/// <summary>Consumed-cost summary of a completed work order.</summary>
public sealed record WorkOrderCostSummary(
    int WorkOrderId,
    string WorkOrderNumber,
    WorkOrderStatus Status,
    int ProducedQuantity,
    decimal ConsumedCost,
    decimal FinishedValue,
    string? CompletionJournalNumber,
    bool Replayed);


/// <summary>One component line of an advisory material proposal.</summary>
public sealed record MaterialProposalLine(
    int ComponentItemId,
    decimal RequiredQuantity,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ShortageQuantity,
    string Verdict);

/// <summary>Advisory material proposal for one released work order; nothing is committed.</summary>
public sealed record MaterialProposal(
    int WorkOrderId,
    string WorkOrderNumber,
    int QuantityToProduce,
    bool Proceed,
    IReadOnlyList<MaterialProposalLine> Lines);

