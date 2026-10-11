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
/// Fixed-asset register and lifecycle: register (Edit), capitalize, run monthly depreciation and dispose
/// (Post). Every money effect is a balanced journal from the finance kernel; reads need View.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/fixed-assets")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class FixedAssetsController(
    IFixedAssetService fixedAssets,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List the company's fixed assets by code.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListAssets(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var assets = await fixedAssets.ListAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FixedAssetResponse>>.CreateSuccess(
            assets.Select(asset => ToResponse(asset, null)).ToArray()));
    }

    /// <summary>Register one asset with its schedule parameters.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RegisterAsset(
        int companyId,
        [FromBody] RegisterFixedAssetWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A fixed-asset body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var asset = await fixedAssets.RegisterAsync(
            new RegisterFixedAssetRequest(
                companyId,
                request.AssetCode ?? string.Empty,
                request.Name ?? string.Empty,
                request.AcquisitionDate,
                request.DepreciationStartDate,
                request.CostAmount,
                request.SalvageValue,
                request.UsefulLifeMonths),
            actor,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetAsset),
            new { companyId, fixedAssetId = asset.Id },
            ApiResponse<FixedAssetResponse>.CreateSuccess(ToResponse(asset, null)));
    }

    /// <summary>One asset with its posted depreciation, accumulated depreciation and net book value.</summary>
    [HttpGet("{fixedAssetId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetAsset(
        int companyId,
        int fixedAssetId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var position = await fixedAssets.GetAsync(companyId, fixedAssetId, cancellationToken);
        if (position is null)
            return NotFound(ApiResponse<object>.CreateFailure("Fixed asset not found in this company."));

        return Ok(ApiResponse<FixedAssetResponse>.CreateSuccess(ToResponse(position.Asset, position)));
    }

    /// <summary>Post the acquisition against the chosen payment or payable account.</summary>
    [HttpPost("{fixedAssetId:int}/capitalize")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CapitalizeAsset(
        int companyId,
        int fixedAssetId,
        [FromBody] CapitalizeFixedAssetWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A capitalization body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var asset = await fixedAssets.CapitalizeAsync(
            companyId, fixedAssetId, request.CreditChartOfAccountId, actor, cancellationToken);
        return Ok(ApiResponse<FixedAssetResponse>.CreateSuccess(ToResponse(asset, null)));
    }

    /// <summary>Post every monthly depreciation whose month ends on or before the as-of date.</summary>
    [HttpPost("depreciation-runs")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RunDepreciation(
        int companyId,
        [FromBody] RunFixedAssetDepreciationWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A depreciation-run body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var run = await fixedAssets.RunDepreciationAsync(companyId, request.AsOfDate, actor, cancellationToken);
        return Ok(ApiResponse<FixedAssetDepreciationRunResponse>.CreateSuccess(new FixedAssetDepreciationRunResponse(
            run.CompanyId,
            run.AsOfDate,
            run.Postings.Select(posting => new FixedAssetDepreciationPostingResponse(
                posting.FixedAssetId,
                posting.PeriodIndex,
                posting.PeriodEnd,
                posting.Amount,
                posting.JournalEntryId,
                posting.Replayed)).ToArray())));
    }

    /// <summary>Dispose of a capitalized asset with proceeds and a mandatory reason.</summary>
    [HttpPost("{fixedAssetId:int}/dispose")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DisposeAsset(
        int companyId,
        int fixedAssetId,
        [FromBody] DisposeFixedAssetWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A disposal body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var asset = await fixedAssets.DisposeAsync(
            companyId,
            fixedAssetId,
            request.DisposalDate,
            request.Proceeds,
            request.ProceedsChartOfAccountId,
            request.Reason ?? string.Empty,
            actor,
            cancellationToken);
        return Ok(ApiResponse<FixedAssetResponse>.CreateSuccess(ToResponse(asset, null)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static FixedAssetResponse ToResponse(FixedAsset asset, FixedAssetPosition? position) => new(
        asset.Id,
        asset.AssetCode,
        asset.Name,
        asset.AcquisitionDate,
        asset.DepreciationStartDate,
        asset.CostAmount,
        asset.SalvageValue,
        asset.UsefulLifeMonths,
        asset.Status.ToString(),
        asset.CapitalizationJournalEntryId,
        asset.DisposalDate,
        asset.DisposalProceeds,
        asset.DisposalJournalEntryId,
        position?.PostedDepreciationPeriods,
        position?.AccumulatedDepreciation,
        position?.NetBookValue);
}

/// <summary>Register body; ownership comes from the route and session.</summary>
public sealed class RegisterFixedAssetWriteRequest
{
    public string? AssetCode { get; init; }
    public string? Name { get; init; }
    public DateOnly AcquisitionDate { get; init; }
    public DateOnly DepreciationStartDate { get; init; }
    public decimal CostAmount { get; init; }
    public decimal SalvageValue { get; init; }
    public int UsefulLifeMonths { get; init; }
}

/// <summary>Capitalization body naming the credit account.</summary>
public sealed class CapitalizeFixedAssetWriteRequest
{
    public int CreditChartOfAccountId { get; init; }
}

/// <summary>Depreciation run body.</summary>
public sealed class RunFixedAssetDepreciationWriteRequest
{
    public DateOnly AsOfDate { get; init; }
}

/// <summary>Disposal body with the mandatory reviewer reason.</summary>
public sealed class DisposeFixedAssetWriteRequest
{
    public DateOnly DisposalDate { get; init; }
    public decimal Proceeds { get; init; }
    public int ProceedsChartOfAccountId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Asset state; depreciation figures are present on the single-asset read only.</summary>
public sealed record FixedAssetResponse(
    int Id,
    string AssetCode,
    string Name,
    DateOnly AcquisitionDate,
    DateOnly DepreciationStartDate,
    decimal CostAmount,
    decimal SalvageValue,
    int UsefulLifeMonths,
    string Status,
    int? CapitalizationJournalEntryId,
    DateOnly? DisposalDate,
    decimal? DisposalProceeds,
    int? DisposalJournalEntryId,
    int? PostedDepreciationPeriods,
    decimal? AccumulatedDepreciation,
    decimal? NetBookValue);

/// <summary>One monthly depreciation posting in a run.</summary>
public sealed record FixedAssetDepreciationPostingResponse(
    int FixedAssetId,
    int PeriodIndex,
    DateOnly PeriodEnd,
    decimal Amount,
    int JournalEntryId,
    bool Replayed);

/// <summary>Depreciation run result; replayed entries were already committed by an earlier run.</summary>
public sealed record FixedAssetDepreciationRunResponse(
    int CompanyId,
    DateOnly AsOfDate,
    IReadOnlyList<FixedAssetDepreciationPostingResponse> Postings);
