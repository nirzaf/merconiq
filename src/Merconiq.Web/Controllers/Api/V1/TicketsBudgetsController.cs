using Asp.Versioning;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Merconiq.Web.Controllers.Api.V1;

/// <summary>Service tickets with priority SLAs and annual budgets versus posted actuals.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class TicketsBudgetsController(
    ITicketService tickets,
    IBudgetService budgets,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create a service ticket; the SLA clock starts at creation.</summary>
    [HttpPost("tickets")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateTicket(
        int companyId,
        [FromBody] CreateTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var ticket = await tickets.CreateTicketAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<TicketResponse>.CreateSuccess(ToResponse(ticket)));
    }

    /// <summary>Start working a ticket.</summary>
    [HttpPost("tickets/{ticketId:int}/start")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Start(
        int companyId, int ticketId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        return Ok(ApiResponse<TicketResponse>.CreateSuccess(
            ToResponse(await tickets.StartTicketAsync(companyId, ticketId, actor, cancellationToken))));
    }

    /// <summary>Resolve a ticket; the SLA breach status derives from the resolution time.</summary>
    [HttpPost("tickets/{ticketId:int}/resolution")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Resolve(
        int companyId, int ticketId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        return Ok(ApiResponse<TicketResponse>.CreateSuccess(
            ToResponse(await tickets.ResolveTicketAsync(companyId, ticketId, actor, cancellationToken))));
    }

    /// <summary>Close a resolved ticket.</summary>
    [HttpPost("tickets/{ticketId:int}/closure")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Close(
        int companyId, int ticketId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        return Ok(ApiResponse<TicketResponse>.CreateSuccess(
            ToResponse(await tickets.CloseTicketAsync(companyId, ticketId, actor, cancellationToken))));
    }

    /// <summary>Open tickets whose SLA resolution clock has already passed.</summary>
    [HttpGet("tickets/breached")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> Breached(int companyId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var breached = await tickets.ListBreachedAsync(companyId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<TicketResponse>>.CreateSuccess(
            breached.Select(ToResponse).ToArray()));
    }

    /// <summary>Set one account's annual budget.</summary>
    [HttpPost("budgets")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SetBudget(
        int companyId,
        [FromBody] SetBudgetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var budget = await budgets.SetBudgetAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<BudgetResponse>.CreateSuccess(new BudgetResponse(
            budget.Id, budget.ChartOfAccountId, budget.Year, budget.AnnualAmount)));
    }

    /// <summary>Budget versus posted actuals for one account and year.</summary>
    [HttpGet("budgets/{budgetId:int}/vs-actual")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> BudgetVersusActual(
        int companyId, int budgetId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        return Ok(ApiResponse<BudgetVersusActual>.CreateSuccess(
            await budgets.GetBudgetVersusActualAsync(companyId, budgetId, cancellationToken)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static TicketResponse ToResponse(ServiceTicket ticket) => new(
        ticket.Id, ticket.TicketNumber, ticket.Subject, ticket.Priority.ToString(),
        ticket.Status.ToString(), ticket.CreatedAtUtc, ticket.ResolutionDueAtUtc,
        ticket.ResolvedAtUtc, ticket.ClosedAtUtc);
}

public sealed record TicketResponse(
    int Id, string TicketNumber, string Subject, string Priority, string Status,
    DateTime CreatedAtUtc, DateTime? ResolutionDueAtUtc, DateTime? ResolvedAtUtc, DateTime? ClosedAtUtc);

public sealed record BudgetResponse(
    int Id, int ChartOfAccountId, int Year, decimal AnnualAmount);
