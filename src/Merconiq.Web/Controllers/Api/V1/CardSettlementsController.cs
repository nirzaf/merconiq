using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>
/// Acquirer settlement batches and their reconciliation to card tenders. Importing a batch needs Edit;
/// settling or withdrawing a card tender needs Approve. Nothing here posts to the ledger.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/card-settlements")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class CardSettlementsController(
    ICardSettlementService settlements,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List imported settlement batches, newest settlement date first.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListBatches(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var batches = await settlements.ListBatchesAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CardSettlementBatchResponse>>.CreateSuccess(
            batches.Select(ToBatchResponse).ToArray()));
    }

    /// <summary>Import one acquirer settlement batch; the net amount is derived as gross less fee.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ImportBatch(
        int companyId,
        [FromBody] ImportCardSettlementWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A settlement batch body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var batch = await settlements.ImportBatchAsync(
            new CardSettlementBatchImportRequest(
                companyId,
                request.Reference ?? string.Empty,
                request.SettlementDate,
                request.GrossAmount,
                request.FeeAmount),
            actor,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetReconciliation),
            new { companyId, batchId = batch.Id },
            ApiResponse<CardSettlementBatchResponse>.CreateSuccess(ToBatchResponse(batch)));
    }

    /// <summary>Reconcile one batch: its matched and unmatched gross, and card tenders still awaiting settlement.</summary>
    [HttpGet("{batchId:int}/reconciliation")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetReconciliation(
        int companyId,
        int batchId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var reconciliation = await settlements.GetReconciliationAsync(companyId, batchId, cancellationToken);
        if (reconciliation is null)
            return NotFound(ApiResponse<object>.CreateFailure("Settlement batch not found in this company."));

        return Ok(ApiResponse<CardSettlementReconciliationResponse>.CreateSuccess(new CardSettlementReconciliationResponse(
            ToBatchResponse(reconciliation.Batch),
            reconciliation.MatchedAmount,
            reconciliation.UnmatchedGrossAmount,
            reconciliation.IsFullySettled,
            reconciliation.AwaitingTenderCount,
            reconciliation.AwaitingTenderAmount,
            reconciliation.AwaitingTenders.Select(tender => new AwaitingCardTenderResponse(
                tender.PosSaleTenderId,
                tender.PosSaleId,
                tender.SaleDate,
                tender.Amount)).ToArray())));
    }

    /// <summary>Attribute one card tender to a settlement batch.</summary>
    [HttpPost("{batchId:int}/tenders/{posSaleTenderId:int}/match")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> MatchTender(
        int companyId,
        int batchId,
        int posSaleTenderId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var match = await settlements.MatchTenderAsync(companyId, batchId, posSaleTenderId, actor, cancellationToken);
        return Ok(ApiResponse<CardSettlementMatchResponse>.CreateSuccess(ToMatchResponse(match)));
    }

    /// <summary>Withdraw a card tender's settlement with a mandatory reason; the withdrawn match stays on record.</summary>
    [HttpPost("{batchId:int}/tenders/{posSaleTenderId:int}/unmatch")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UnmatchTender(
        int companyId,
        int batchId,
        int posSaleTenderId,
        [FromBody] UnmatchCardTenderWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An unmatch body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var match = await settlements.UnmatchTenderAsync(
            companyId, batchId, posSaleTenderId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<CardSettlementMatchResponse>.CreateSuccess(ToMatchResponse(match)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static CardSettlementBatchResponse ToBatchResponse(CardSettlementBatch batch) => new(
        batch.Id,
        batch.Reference,
        batch.SettlementDate,
        batch.GrossAmount,
        batch.FeeAmount,
        batch.NetAmount,
        batch.ImportedBy,
        batch.ImportedAtUtc);

    private static CardSettlementMatchResponse ToMatchResponse(CardSettlementMatch match) => new(
        match.Id,
        match.CardSettlementBatchId,
        match.PosSaleTenderId,
        match.Amount,
        match.IsActive,
        match.MatchedBy,
        match.MatchedAtUtc,
        match.UnmatchedBy,
        match.UnmatchedAtUtc,
        match.UnmatchReason);
}

/// <summary>Settlement import body; the processor's gross and fee are supplied, the net is derived.</summary>
public sealed class ImportCardSettlementWriteRequest
{
    public string? Reference { get; init; }
    public DateOnly SettlementDate { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal FeeAmount { get; init; }
}

/// <summary>Withdrawal body with the mandatory reviewer reason.</summary>
public sealed class UnmatchCardTenderWriteRequest
{
    public string? Reason { get; init; }
}

/// <summary>A settlement batch as the processor reported it.</summary>
public sealed record CardSettlementBatchResponse(
    int Id,
    string Reference,
    DateOnly SettlementDate,
    decimal GrossAmount,
    decimal FeeAmount,
    decimal NetAmount,
    string ImportedBy,
    DateTime ImportedAtUtc);

/// <summary>Reconciliation of one batch against the card tenders it has paid.</summary>
public sealed record CardSettlementReconciliationResponse(
    CardSettlementBatchResponse Batch,
    decimal MatchedAmount,
    decimal UnmatchedGrossAmount,
    bool IsFullySettled,
    int AwaitingTenderCount,
    decimal AwaitingTenderAmount,
    IReadOnlyList<AwaitingCardTenderResponse> AwaitingTenders);

/// <summary>A card tender taken on or before the batch date that no active settlement covers yet.</summary>
public sealed record AwaitingCardTenderResponse(
    int PosSaleTenderId,
    int PosSaleId,
    DateOnly SaleDate,
    decimal Amount);

/// <summary>A tender-to-batch settlement record, active or withdrawn.</summary>
public sealed record CardSettlementMatchResponse(
    int Id,
    int BatchId,
    int PosSaleTenderId,
    decimal Amount,
    bool IsActive,
    string MatchedBy,
    DateTime MatchedAtUtc,
    string? UnmatchedBy,
    DateTime? UnmatchedAtUtc,
    string? UnmatchReason);
