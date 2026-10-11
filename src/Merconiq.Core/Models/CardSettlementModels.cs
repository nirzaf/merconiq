using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Input for one imported settlement batch; the net amount is derived, never supplied.</summary>
public sealed record CardSettlementBatchImportRequest(
    int CompanyId,
    string Reference,
    DateOnly SettlementDate,
    decimal GrossAmount,
    decimal FeeAmount);

/// <summary>A card tender that no active settlement match explains yet.</summary>
public sealed record AwaitingCardTender(
    int PosSaleTenderId,
    int PosSaleId,
    DateOnly SaleDate,
    decimal Amount);

/// <summary>
/// Settlement reconciliation for one batch. Matched tenders cover part or all of the gross; the batch is
/// fully settled when the matched total equals the gross, and tenders taken on or before the settlement
/// date that remain unmatched are listed as still awaiting settlement.
/// </summary>
public sealed record CardSettlementReconciliation(
    CardSettlementBatch Batch,
    decimal MatchedAmount,
    decimal UnmatchedGrossAmount,
    bool IsFullySettled,
    int AwaitingTenderCount,
    decimal AwaitingTenderAmount,
    IReadOnlyList<AwaitingCardTender> AwaitingTenders);
