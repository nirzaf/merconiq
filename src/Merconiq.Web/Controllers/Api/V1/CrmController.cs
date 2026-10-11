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
/// Leads, opportunities, activities and the pipeline summary. Reads need View; pipeline changes need Edit.
/// None of these routes post to the ledger or move stock.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/crm")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class CrmController(ICrmService crm, ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>List the company's leads, newest first.</summary>
    [HttpGet("leads")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListLeads(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();
        var leads = await crm.ListLeadsAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CrmLeadResponse>>.CreateSuccess(leads.Select(ToResponse).ToArray()));
    }

    /// <summary>Capture a new lead.</summary>
    [HttpPost("leads")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateLead(int companyId, [FromBody] CreateCrmLeadRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A lead body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var lead = await crm.CreateLeadAsync(
            companyId, request.Name ?? string.Empty, request.Email, request.Phone, request.Source ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<CrmLeadResponse>.CreateSuccess(ToResponse(lead)));
    }

    /// <summary>Mark a new lead as qualified.</summary>
    [HttpPost("leads/{leadId:int}/qualification")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QualifyLead(int companyId, int leadId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        return Ok(ApiResponse<CrmLeadResponse>.CreateSuccess(ToResponse(await crm.QualifyLeadAsync(companyId, leadId, actor, cancellationToken))));
    }

    /// <summary>Disqualify an unconverted lead with a reason.</summary>
    [HttpPost("leads/{leadId:int}/disqualification")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DisqualifyLead(int companyId, int leadId, [FromBody] CrmReasonRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A reason body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        return Ok(ApiResponse<CrmLeadResponse>.CreateSuccess(ToResponse(
            await crm.DisqualifyLeadAsync(companyId, leadId, request.Reason ?? string.Empty, actor, cancellationToken))));
    }

    /// <summary>Convert a qualified lead into an open opportunity.</summary>
    [HttpPost("leads/{leadId:int}/conversion")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConvertLead(int companyId, int leadId, [FromBody] CreateCrmOpportunityRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An opportunity body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        var opportunity = await crm.ConvertLeadAsync(
            companyId, leadId, request.Title ?? string.Empty, request.ExpectedValue, request.ExpectedCloseDate, request.Owner ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<CrmOpportunityResponse>.CreateSuccess(ToResponse(opportunity)));
    }

    /// <summary>List the company's opportunities, newest first.</summary>
    [HttpGet("opportunities")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListOpportunities(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();
        var opportunities = await crm.ListOpportunitiesAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CrmOpportunityResponse>>.CreateSuccess(opportunities.Select(ToResponse).ToArray()));
    }

    /// <summary>Create an open opportunity without a lead.</summary>
    [HttpPost("opportunities")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateOpportunity(int companyId, [FromBody] CreateCrmOpportunityRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An opportunity body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        var opportunity = await crm.CreateOpportunityAsync(
            companyId, request.Title ?? string.Empty, request.ExpectedValue, request.ExpectedCloseDate, request.Owner ?? string.Empty, actor, cancellationToken);
        return Ok(ApiResponse<CrmOpportunityResponse>.CreateSuccess(ToResponse(opportunity)));
    }

    /// <summary>Move an open opportunity between Prospecting and Proposal.</summary>
    [HttpPost("opportunities/{opportunityId:int}/stage")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AdvanceOpportunity(int companyId, int opportunityId, [FromBody] AdvanceCrmOpportunityRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A stage body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        return Ok(ApiResponse<CrmOpportunityResponse>.CreateSuccess(ToResponse(
            await crm.AdvanceOpportunityAsync(companyId, opportunityId, request.Stage, actor, cancellationToken))));
    }

    /// <summary>Win an open opportunity against one of the company's customers.</summary>
    [HttpPost("opportunities/{opportunityId:int}/won")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> WinOpportunity(int companyId, int opportunityId, [FromBody] WinCrmOpportunityRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A customer body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        return Ok(ApiResponse<CrmOpportunityResponse>.CreateSuccess(ToResponse(
            await crm.WinOpportunityAsync(companyId, opportunityId, request.CustomerId, actor, cancellationToken))));
    }

    /// <summary>Lose an open opportunity with a reason.</summary>
    [HttpPost("opportunities/{opportunityId:int}/lost")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LoseOpportunity(int companyId, int opportunityId, [FromBody] CrmReasonRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("A reason body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        return Ok(ApiResponse<CrmOpportunityResponse>.CreateSuccess(ToResponse(
            await crm.LoseOpportunityAsync(companyId, opportunityId, request.Reason ?? string.Empty, actor, cancellationToken))));
    }

    /// <summary>Log an activity against one lead or one opportunity.</summary>
    [HttpPost("activities")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> LogActivity(int companyId, [FromBody] LogCrmActivityRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
            return BadRequest(ApiResponse<object>.CreateFailure("An activity body is required."));
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();
        var activity = await crm.LogActivityAsync(
            companyId, request.LeadId, request.OpportunityId, request.Kind, request.Summary ?? string.Empty, request.OccurredAtUtc, actor, cancellationToken);
        return Ok(ApiResponse<CrmActivityResponse>.CreateSuccess(ToResponse(activity)));
    }

    /// <summary>Activities for one lead or one opportunity, oldest first.</summary>
    [HttpGet("activities")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> ListActivities(int companyId, [FromQuery] int? leadId, [FromQuery] int? opportunityId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();
        var activities = await crm.ListActivitiesAsync(companyId, leadId, opportunityId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<CrmActivityResponse>>.CreateSuccess(activities.Select(ToResponse).ToArray()));
    }

    /// <summary>Pipeline count and expected value by stage, with the open value.</summary>
    [HttpGet("pipeline")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetPipeline(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();
        var summary = await crm.GetPipelineSummaryAsync(companyId, cancellationToken);
        return Ok(ApiResponse<CrmPipelineSummaryResponse>.CreateSuccess(new CrmPipelineSummaryResponse(
            summary.Stages.Select(row => new CrmPipelineStageResponse(row.Stage.ToString(), row.Count, row.ExpectedValue)).ToArray(),
            summary.OpenExpectedValue)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static CrmLeadResponse ToResponse(CrmLead lead) => new(
        lead.Id, lead.Name, lead.Email, lead.Phone, lead.Source, lead.Status.ToString(), lead.DisqualifiedReason);

    private static CrmOpportunityResponse ToResponse(CrmOpportunity opportunity) => new(
        opportunity.Id, opportunity.Title, opportunity.LeadId, opportunity.CustomerId, opportunity.Stage.ToString(),
        opportunity.ExpectedValue, opportunity.ExpectedCloseDate, opportunity.Owner, opportunity.LostReason);

    private static CrmActivityResponse ToResponse(CrmActivity activity) => new(
        activity.Id, activity.LeadId, activity.OpportunityId, activity.Kind.ToString(), activity.Summary, activity.OccurredAtUtc, activity.RecordedByUser);
}

/// <summary>Lead capture body.</summary>
public sealed class CreateCrmLeadRequest
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Source { get; init; }
}

/// <summary>Opportunity body used for direct creation and for lead conversion.</summary>
public sealed class CreateCrmOpportunityRequest
{
    public string? Title { get; init; }
    public decimal ExpectedValue { get; init; }
    public DateOnly ExpectedCloseDate { get; init; }
    public string? Owner { get; init; }
}

/// <summary>Stage move body; only Prospecting and Proposal are accepted here.</summary>
public sealed class AdvanceCrmOpportunityRequest
{
    public CrmOpportunityStage Stage { get; init; }
}

/// <summary>Win body naming the existing customer.</summary>
public sealed class WinCrmOpportunityRequest
{
    public int CustomerId { get; init; }
}

/// <summary>Reason body for disqualification and loss.</summary>
public sealed class CrmReasonRequest
{
    public string? Reason { get; init; }
}

/// <summary>Activity body naming exactly one of lead or opportunity.</summary>
public sealed class LogCrmActivityRequest
{
    public int? LeadId { get; init; }
    public int? OpportunityId { get; init; }
    public CrmActivityKind Kind { get; init; }
    public string? Summary { get; init; }
    public DateTime OccurredAtUtc { get; init; }
}

/// <summary>Lead state.</summary>
public sealed record CrmLeadResponse(int Id, string Name, string? Email, string? Phone, string Source, string Status, string? DisqualifiedReason);

/// <summary>Opportunity state.</summary>
public sealed record CrmOpportunityResponse(
    int Id, string Title, int? LeadId, int? CustomerId, string Stage, decimal ExpectedValue, DateOnly ExpectedCloseDate, string Owner, string? LostReason);

/// <summary>One logged activity.</summary>
public sealed record CrmActivityResponse(
    int Id, int? LeadId, int? OpportunityId, string Kind, string Summary, DateTime OccurredAtUtc, string RecordedBy);

/// <summary>Pipeline stage row.</summary>
public sealed record CrmPipelineStageResponse(string Stage, int Count, decimal ExpectedValue);

/// <summary>Pipeline totals with the open value.</summary>
public sealed record CrmPipelineSummaryResponse(IReadOnlyList<CrmPipelineStageResponse> Stages, decimal OpenExpectedValue);
