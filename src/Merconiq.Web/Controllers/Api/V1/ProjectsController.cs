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

/// <summary>Projects, tasks and timesheets. Approval freezes billable evidence; nothing posts.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/projects")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class ProjectsController(
    IProjectService projects,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create a project.</summary>
    [HttpPost]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Create(
        int companyId,
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var project = await projects.CreateProjectAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<ProjectResponse>.CreateSuccess(ToResponse(project)));
    }

    /// <summary>Create a task inside a project.</summary>
    [HttpPost("{projectId:int}/tasks")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateTask(
        int companyId,
        int projectId,
        [FromBody] ProjectTaskWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var task = await projects.CreateTaskAsync(
            new CreateProjectTaskRequest(companyId, projectId, request.Name), actor, cancellationToken);
        return Ok(ApiResponse<ProjectTaskResponse>.CreateSuccess(new ProjectTaskResponse(
            task.Id, task.ProjectId, task.Name, task.Status.ToString())));
    }

    /// <summary>Log time against a task as the calling user.</summary>
    [HttpPost("time")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogTime(
        int companyId,
        [FromBody] LogTimeWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null || request is null)
            return Unauthorized();

        var entry = await projects.LogTimeAsync(
            new LogTimeRequest(companyId, request.ProjectTaskId, request.WorkDate,
                request.Hours, request.Billable, request.HourlyRate, request.Notes),
            actor,
            cancellationToken);
        return Ok(ApiResponse<TimesheetEntryResponse>.CreateSuccess(ToResponse(entry)));
    }

    /// <summary>Approve one submitted time line (freezes it as evidence).</summary>
    [HttpPost("time/{entryId:int}/approval")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ApproveTime(
        int companyId, int entryId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var entry = await projects.ApproveTimeAsync(companyId, entryId, actor, cancellationToken);
        return Ok(ApiResponse<TimesheetEntryResponse>.CreateSuccess(ToResponse(entry)));
    }

    /// <summary>Reject one draft or submitted time line with a mandatory reason.</summary>
    [HttpPost("time/{entryId:int}/rejection")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RejectTime(
        int companyId,
        int entryId,
        [FromBody] RejectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var entry = await projects.RejectTimeAsync(
            companyId, entryId, request?.Reason ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<TimesheetEntryResponse>.CreateSuccess(ToResponse(entry)));
    }

    /// <summary>Approved-only profitability for one project.</summary>
    [HttpGet("{projectId:int}/profitability")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetProfitability(
        int companyId, int projectId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var profitability = await projects.GetProfitabilityAsync(companyId, projectId, cancellationToken);
        return Ok(ApiResponse<ProjectProfitability>.CreateSuccess(profitability));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static ProjectResponse ToResponse(Project project) => new(
        project.Id, project.CompanyId, project.Code, project.Name,
        project.CustomerId, project.Status.ToString(), project.Notes);

    private static TimesheetEntryResponse ToResponse(TimesheetEntry entry) => new(
        entry.Id, entry.ProjectId, entry.ProjectTaskId, entry.WorkerId, entry.WorkDate,
        entry.Hours, entry.Billable, entry.HourlyRate, entry.Status.ToString(),
        entry.ApprovedAtUtc, entry.ApprovedBy, entry.RejectionReason);
}

public sealed class ProjectTaskWriteRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;
}

public sealed class LogTimeWriteRequest
{
    [Range(1, int.MaxValue)]
    public int ProjectTaskId { get; init; }

    public DateOnly WorkDate { get; init; }

    [Range(0.01, 24)]
    public decimal Hours { get; init; }

    public bool Billable { get; init; }

    [Range(0, double.MaxValue)]
    public decimal HourlyRate { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

public sealed class RejectionRequest
{
    [Required, StringLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record ProjectResponse(
    int Id, int CompanyId, string Code, string Name, int? CustomerId, string Status, string? Notes);

public sealed record ProjectTaskResponse(
    int Id, int ProjectId, string Name, string Status);

public sealed record TimesheetEntryResponse(
    int Id, int ProjectId, int ProjectTaskId, string WorkerId, DateOnly WorkDate,
    decimal Hours, bool Billable, decimal HourlyRate, string Status,
    DateTime? ApprovedAtUtc, string? ApprovedBy, string? RejectionReason);
