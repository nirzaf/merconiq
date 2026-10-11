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
/// Till approval for POS shift closes held above a register's variance threshold. Approving or rejecting a held
/// close needs Approve; setting a register's threshold needs Administer. Approval posts the over/short journal.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/pos")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class TillApprovalController(
    ITillApprovalService tillApproval,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Approve a held shift close; posts its cash over/short journal once and closes the shift.</summary>
    [HttpPost("shifts/{shiftId:int}/close-approval")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApproveClose(
        int companyId,
        int shiftId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var result = await tillApproval.ApproveCloseAsync(companyId, shiftId, actor, cancellationToken);
        return Ok(ApiResponse<TillApprovalResponse>.CreateSuccess(ToResponse(result)));
    }

    /// <summary>Send a held shift close back to the cashier for a recount, with a mandatory reason.</summary>
    [HttpPost("shifts/{shiftId:int}/close-rejection")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RejectClose(
        int companyId,
        int shiftId,
        [FromBody] RejectShiftCloseWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A rejection body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var result = await tillApproval.RejectCloseAsync(
            companyId, shiftId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<TillApprovalResponse>.CreateSuccess(ToResponse(result)));
    }

    /// <summary>Set or clear a register's variance threshold; null means its closes never wait for review.</summary>
    [HttpPut("registers/{registerId:int}/variance-threshold")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetVarianceThreshold(
        int companyId,
        int registerId,
        [FromBody] SetRegisterVarianceThresholdWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A threshold body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var register = await tillApproval.SetVarianceThresholdAsync(
            companyId, registerId, request.VarianceApprovalThreshold, actor, cancellationToken);
        return Ok(ApiResponse<RegisterThresholdResponse>.CreateSuccess(new RegisterThresholdResponse(
            register.Id, register.Name, register.VarianceApprovalThreshold)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static TillApprovalResponse ToResponse(TillApprovalResult result) => new(
        result.PosShiftId,
        result.Status.ToString(),
        result.ExpectedCash,
        result.CountedCash,
        result.OverShortValue,
        result.OverShortJournalNumber,
        result.ClosedBy,
        result.ApprovedBy,
        result.ApprovedAtUtc,
        result.RejectedBy,
        result.RejectionReason);
}

/// <summary>Rejection body with the mandatory reviewer reason.</summary>
public sealed class RejectShiftCloseWriteRequest
{
    public string? Reason { get; init; }
}

/// <summary>Threshold body; a null value clears the threshold.</summary>
public sealed class SetRegisterVarianceThresholdWriteRequest
{
    public decimal? VarianceApprovalThreshold { get; init; }
}

/// <summary>Shift state after an approval decision.</summary>
public sealed record TillApprovalResponse(
    int ShiftId,
    string Status,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal OverShortValue,
    string? OverShortJournalNumber,
    string? ClosedBy,
    string? ApprovedBy,
    DateTime? ApprovedAtUtc,
    string? RejectedBy,
    string? RejectionReason);

/// <summary>Register with its current approval threshold.</summary>
public sealed record RegisterThresholdResponse(
    int RegisterId,
    string Name,
    decimal? VarianceApprovalThreshold);
