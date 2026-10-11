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
/// Company-scoped fiscal-calendar controls. Fiscal years are created administratively; closing
/// a period and reopening a closed period are separate authorized, audited actions.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class FiscalPeriodsController(
    IFiscalCalendarService fiscalCalendar,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List fiscal years with their periods for one authorized company.</summary>
    [HttpGet("fiscal-years")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetFiscalYears(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var years = await fiscalCalendar.ListFiscalYearsAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<FiscalYearResponse>>.CreateSuccess(
            years.Select(ToResponse).ToArray()));
    }

    /// <summary>Create a fiscal year and its monthly periods.</summary>
    [HttpPost("fiscal-years")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateFiscalYear(
        int companyId,
        [FromBody] CreateFiscalYearWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || companyId != request.CompanyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();

        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var year = await fiscalCalendar.CreateFiscalYearAsync(
            new CreateFiscalYearRequest(companyId, request.Name, request.StartDate, request.EndDate),
            actor,
            cancellationToken);
        return CreatedAtAction(
            nameof(GetFiscalYears),
            new { companyId },
            ApiResponse<FiscalYearResponse>.CreateSuccess(ToResponse(year)));
    }

    /// <summary>Close one open fiscal period; closed periods reject new journals.</summary>
    [HttpPost("fiscal-periods/{periodId:int}/close")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ClosePeriod(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var period = await fiscalCalendar.ClosePeriodAsync(companyId, periodId, actor, cancellationToken);
        return Ok(ApiResponse<FiscalPeriodResponse>.CreateSuccess(ToResponse(period)));
    }

    /// <summary>Reopen one closed fiscal period with a mandatory recorded reason.</summary>
    [HttpPost("fiscal-periods/{periodId:int}/reopen")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReopenPeriod(
        int companyId,
        int periodId,
        [FromBody] ReopenFiscalPeriodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var period = await fiscalCalendar.ReopenPeriodAsync(
            companyId, periodId, request?.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<FiscalPeriodResponse>.CreateSuccess(ToResponse(period)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static FiscalYearResponse ToResponse(FiscalYear year) => new(
        year.Id,
        year.CompanyId,
        year.Name,
        year.StartDate,
        year.EndDate,
        year.Periods.OrderBy(period => period.PeriodNumber).Select(ToResponse).ToArray());

    private static FiscalPeriodResponse ToResponse(FiscalPeriod period) => new(
        period.Id,
        period.PeriodNumber,
        period.Name,
        period.StartDate,
        period.EndDate,
        period.Status.ToString(),
        period.ClosedAtUtc,
        period.ClosedBy,
        period.ReopenedAtUtc,
        period.ReopenedBy,
        period.ReopenReason);
}

/// <summary>Fiscal-year creation fields; ownership is taken from the route and session.</summary>
public sealed class CreateFiscalYearWriteRequest
{
    public int CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
}

/// <summary>Reopen request carrying the mandatory reviewer reason.</summary>
public sealed class ReopenFiscalPeriodRequest
{
    public string Reason { get; init; } = string.Empty;
}

/// <summary>A fiscal year with its ordered periods.</summary>
public sealed record FiscalYearResponse(
    int Id,
    int CompanyId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<FiscalPeriodResponse> Periods);

/// <summary>One controllable fiscal period.</summary>
public sealed record FiscalPeriodResponse(
    int Id,
    int PeriodNumber,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTime? ClosedAtUtc,
    string? ClosedBy,
    DateTime? ReopenedAtUtc,
    string? ReopenedBy,
    string? ReopenReason);
