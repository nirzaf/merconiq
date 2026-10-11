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
/// Per-period posting locks on individual chart accounts. Locking an account inside a fiscal period
/// blocks every journal that touches it for that period, including reversals. Locking needs the
/// same approval ceiling as closing a period; releasing needs the reopen ceiling and a reason.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/fiscal-periods/{periodId:int}/account-locks")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class FiscalAccountLocksController(
    IFiscalAccountLockService accountLocks,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List the account lock rows recorded for one fiscal period.</summary>
    [HttpGet]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetLocks(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var locks = await accountLocks.ListLocksAsync(companyId, periodId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FiscalAccountLockResponse>>.CreateSuccess(
            locks.Select(ToResponse).ToArray()));
    }

    /// <summary>Lock one posting account for the fiscal period with a mandatory reason.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LockAccount(
        int companyId,
        int periodId,
        [FromBody] LockFiscalAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A lock request body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var locked = await accountLocks.LockAccountAsync(
            companyId, periodId, request.ChartOfAccountId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<FiscalAccountLockResponse>.CreateSuccess(ToResponse(locked)));
    }

    /// <summary>Release an active account lock; the release reason is recorded and audited.</summary>
    [HttpPost("{chartOfAccountId:int}/release")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReleaseAccount(
        int companyId,
        int periodId,
        int chartOfAccountId,
        [FromBody] ReleaseFiscalAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A release request body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var released = await accountLocks.ReleaseAccountAsync(
            companyId, periodId, chartOfAccountId, request.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<FiscalAccountLockResponse>.CreateSuccess(ToResponse(released)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static FiscalAccountLockResponse ToResponse(FiscalPeriodAccountLock entry) => new(
        entry.ChartOfAccountId,
        entry.FiscalPeriodId,
        entry.IsLocked,
        entry.LockedAtUtc,
        entry.LockedBy,
        entry.LockReason,
        entry.ReleasedAtUtc,
        entry.ReleasedBy,
        entry.ReleaseReason);
}

/// <summary>Lock request naming the posting account; the period and company come from the route.</summary>
public sealed class LockFiscalAccountRequest
{
    public int ChartOfAccountId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Release request carrying the mandatory reviewer reason.</summary>
public sealed class ReleaseFiscalAccountRequest
{
    public string? Reason { get; init; }
}

/// <summary>Current lock state for one account in one fiscal period.</summary>
public sealed record FiscalAccountLockResponse(
    int ChartOfAccountId,
    int FiscalPeriodId,
    bool IsLocked,
    DateTime LockedAtUtc,
    string LockedBy,
    string LockReason,
    DateTime? ReleasedAtUtc,
    string? ReleasedBy,
    string? ReleaseReason);
