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

/// <summary>HR core: employees, attendance and leave. Leave approval requires Approve.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/hr")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class HumanResourcesController(
    IHumanResourcesService hr,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create an employee.</summary>
    [HttpPost("employees")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateEmployee(
        int companyId,
        [FromBody] CreateEmployeeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var employee = await hr.CreateEmployeeAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<EmployeeResponse>.CreateSuccess(ToResponse(employee)));
    }

    /// <summary>Record check-in for one employee (one open record per employee per day).</summary>
    [HttpPost("employees/{employeeId:int}/check-in")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CheckIn(
        int companyId,
        int employeeId,
        [FromBody] CheckInWriteRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var record = await hr.CheckInAsync(companyId, employeeId, request?.Notes, actor, cancellationToken);
        return Ok(ApiResponse<AttendanceResponse>.CreateSuccess(ToResponse(record)));
    }

    /// <summary>Close the open attendance record for one employee.</summary>
    [HttpPost("employees/{employeeId:int}/check-out")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CheckOut(
        int companyId, int employeeId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var record = await hr.CheckOutAsync(companyId, employeeId, actor, cancellationToken);
        return Ok(ApiResponse<AttendanceResponse>.CreateSuccess(ToResponse(record)));
    }

    /// <summary>Request leave for one employee.</summary>
    [HttpPost("leave-requests")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RequestLeave(
        int companyId,
        [FromBody] LeaveRequestWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var leave = await hr.RequestLeaveAsync(
            new RequestLeaveRequest(companyId, request.EmployeeId, request.LeaveType,
                request.StartDate, request.EndDate, request.Reason),
            actor,
            cancellationToken);
        return Ok(ApiResponse<LeaveResponse>.CreateSuccess(ToResponse(leave)));
    }

    /// <summary>Approve or reject one pending leave request; rejection requires a reason.</summary>
    [HttpPost("leave-requests/{leaveRequestId:int}/decision")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DecideLeave(
        int companyId,
        int leaveRequestId,
        [FromBody] LeaveDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var leave = await hr.DecideLeaveAsync(
            companyId, leaveRequestId, request.Approve, request.DecisionReason, actor, cancellationToken);
        return Ok(ApiResponse<LeaveResponse>.CreateSuccess(ToResponse(leave)));
    }

    /// <summary>Approved leave days per type for one employee and year.</summary>
    [HttpGet("employees/{employeeId:int}/leave-balance")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetLeaveBalance(
        int companyId,
        int employeeId,
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var balance = await hr.GetLeaveBalanceAsync(companyId, employeeId, year, cancellationToken);
        return Ok(ApiResponse<LeaveBalance>.CreateSuccess(balance));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static EmployeeResponse ToResponse(Employee employee) => new(
        employee.Id, employee.CompanyId, employee.Code, employee.FullName,
        employee.Department, employee.Position, employee.HiredOn, employee.IsActive);

    private static AttendanceResponse ToResponse(AttendanceRecord record) => new(
        record.Id, record.EmployeeId, record.WorkDate, record.CheckInAtUtc, record.CheckOutAtUtc);

    private static LeaveResponse ToResponse(LeaveRequest leave) => new(
        leave.Id, leave.EmployeeId, leave.LeaveType.ToString(), leave.StartDate, leave.EndDate,
        leave.Days, leave.Status.ToString(), leave.DecidedBy, leave.DecisionReason);
}

public sealed class CheckInWriteRequest
{
    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class LeaveRequestWriteRequest
{
    public int CompanyId { get; init; }

    [Range(1, int.MaxValue)]
    public int EmployeeId { get; init; }

    public LeaveType LeaveType { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class LeaveDecisionRequest
{
    public bool Approve { get; init; }

    [StringLength(500)]
    public string? DecisionReason { get; init; }
}

public sealed record EmployeeResponse(
    int Id, int CompanyId, string Code, string FullName,
    string? Department, string? Position, DateOnly HiredOn, bool IsActive);

public sealed record AttendanceResponse(
    int Id, int EmployeeId, DateOnly WorkDate, DateTime CheckInAtUtc, DateTime? CheckOutAtUtc);

public sealed record LeaveResponse(
    int Id, int EmployeeId, string LeaveType, DateOnly StartDate, DateOnly EndDate,
    decimal Days, string Status, string? DecidedBy, string? DecisionReason);
