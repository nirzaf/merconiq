using System.ComponentModel.DataAnnotations;
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
/// Payroll: salary structures and monthly pay runs. Posting commits one immutable journal
/// through the finance kernel and requires the company Post capability.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/payroll")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class PayrollController(
    IPayrollService payroll,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Set (upsert) one employee's salary structure effective from a month.</summary>
    [HttpPost("salary-structures")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetSalaryStructure(
        int companyId,
        [FromBody] SetSalaryStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var structure = await payroll.SetSalaryStructureAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<SalaryStructureResponse>.CreateSuccess(new SalaryStructureResponse(
            structure.Id, structure.EmployeeId, structure.EffectiveFrom,
            structure.BaseSalary, structure.Allowances, structure.IsActive)));
    }

    /// <summary>Draft one pay run from the effective structures for the month.</summary>
    [HttpPost("pay-runs")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DraftPayRun(
        int companyId,
        [FromBody] PayRunDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var run = await payroll.DraftPayRunAsync(companyId, request.Period, actor, cancellationToken);
        return Ok(ApiResponse<PayRunResponse>.CreateSuccess(ToResponse(run)));
    }

    /// <summary>Post one draft pay run; one immutable journal commits exactly once per month.</summary>
    [HttpPost("pay-runs/{payRunId:int}/posting")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostPayRun(
        int companyId, int payRunId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var run = await payroll.PostPayRunAsync(companyId, payRunId, actor, cancellationToken);
        return Ok(ApiResponse<PayRunResponse>.CreateSuccess(ToResponse(run)));
    }

    /// <summary>Reconcile one posted run: declared totals versus its posted journal lines.</summary>
    [HttpGet("pay-runs/{payRunId:int}/settlement")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetSettlement(
        int companyId, int payRunId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var report = await payroll.GetSettlementReportAsync(companyId, payRunId, cancellationToken);
        return Ok(ApiResponse<PayRunSettlementReport>.CreateSuccess(report));
    }

    /// <summary>Controlled correction: reverse one posted run with a mandatory reason.</summary>
    [HttpPost("pay-runs/{payRunId:int}/reversal")]
    [Authorize(Policy = CapabilityPolicies.Administer)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ReversePayRun(
        int companyId,
        int payRunId,
        [FromBody] PayRunReversalRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Administer))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var run = await payroll.ReversePayRunAsync(companyId, payRunId, request.Reason, actor, cancellationToken);
        return Ok(ApiResponse<PayRunResponse>.CreateSuccess(ToResponse(run)));
    }

    /// <summary>Get one pay run with its payslips.</summary>
    [HttpGet("pay-runs/{payRunId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetPayRun(
        int companyId, int payRunId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var run = await payroll.GetPayRunAsync(companyId, payRunId, cancellationToken);
        return run is null
            ? NotFound(ApiResponse<object>.CreateFailure("Pay run not found in this company."))
            : Ok(ApiResponse<PayRunResponse>.CreateSuccess(ToResponse(run)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static PayRunResponse ToResponse(PayRun run) => new(
        run.Id,
        run.Period,
        run.Status.ToString(),
        run.Payslips.OrderBy(slip => slip.EmployeeId).Select(slip => new PayslipResponse(
            slip.Id, slip.EmployeeId, slip.BaseSalary, slip.Allowances,
            slip.Deductions, slip.Gross, slip.Net, slip.Notes)).ToArray(),
        run.TotalGross,
        run.TotalDeductions,
        run.TotalNet);
}

public sealed class PayRunDraftRequest
{
    /// <summary>Any date inside the payroll month; normalized to the first of the month.</summary>
    public DateOnly Period { get; init; }
}

public sealed record SalaryStructureResponse(
    int Id, int EmployeeId, DateOnly EffectiveFrom, decimal BaseSalary, decimal Allowances, bool IsActive);

public sealed record PayRunResponse(
    int Id, DateOnly Period, string Status, IReadOnlyList<PayslipResponse> Payslips,
    decimal TotalGross, decimal TotalDeductions, decimal TotalNet);

public sealed record PayslipResponse(
    int Id, int EmployeeId, decimal BaseSalary, decimal Allowances,
    decimal Deductions, decimal Gross, decimal Net, string? Notes);

public sealed class PayRunReversalRequest
{
    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;
}
