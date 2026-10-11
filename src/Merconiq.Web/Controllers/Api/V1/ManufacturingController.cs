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
/// Manufacturing: BOMs and work orders. Completion consumes components and receives finished
/// goods through the stock ledger and posts the paired WIP journals once (Post capability).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/companies/{companyId:int}/manufacturing")]
[Produces("application/json")]
[Authorize(Policy = "Api")]
[EnableRateLimiting("Api")]
public sealed class ManufacturingController(
    IManufacturingService manufacturing,
    ICurrentUserAuthorization authorization) : ControllerBase
{
    /// <summary>Create one BOM version for a finished item.</summary>
    [HttpPost("boms")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateBom(
        int companyId,
        [FromBody] CreateBomRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var bom = await manufacturing.CreateBillOfMaterialsAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<BomResponse>.CreateSuccess(new BomResponse(
            bom.Id, bom.ItemId, bom.Version, bom.IsActive,
            bom.Lines.OrderBy(line => line.LineNumber).Select(line => new BomLineResponse(
                line.Id, line.ComponentItemId, line.QuantityPerUnit)).ToArray())));
    }

    /// <summary>Create a work order against a BOM (Draft).</summary>
    [HttpPost("work-orders")]
    [Authorize(Policy = CapabilityPolicies.Edit)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateWorkOrder(
        int companyId,
        [FromBody] CreateWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Edit))
            return Forbid();
        if (request is null || request.CompanyId != companyId)
            return BadRequest(ApiResponse<object>.CreateFailure("CompanyId must match the route."));
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var workOrder = await manufacturing.CreateWorkOrderAsync(request, actor, cancellationToken);
        return Ok(ApiResponse<WorkOrderResponse>.CreateSuccess(ToResponse(workOrder)));
    }

    /// <summary>Release a draft work order for production.</summary>
    [HttpPost("work-orders/{workOrderId:int}/release")]
    [Authorize(Policy = CapabilityPolicies.Approve)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Release(
        int companyId, int workOrderId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Approve))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var workOrder = await manufacturing.ReleaseWorkOrderAsync(companyId, workOrderId, actor, cancellationToken);
        return Ok(ApiResponse<WorkOrderResponse>.CreateSuccess(ToResponse(workOrder)));
    }

    /// <summary>
    /// Complete a released work order: consume components, receive finished goods, and post the
    /// paired WIP journals exactly once; repeated calls replay the completed order.
    /// </summary>
    [HttpPost("work-orders/{workOrderId:int}/completion")]
    [Authorize(Policy = CapabilityPolicies.Post)]
    [ValidateAntiForgeryToken]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Complete(
        int companyId, int workOrderId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post))
            return Forbid();
        var actor = RequireActor();
        if (actor is null)
            return Unauthorized();

        var mutationScope = new StockMutationScope(
            companyId,
            () => authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.Post));

        var workOrder = await manufacturing.CompleteWorkOrderAsync(
            companyId, workOrderId, mutationScope, actor, cancellationToken);
        return Ok(ApiResponse<WorkOrderResponse>.CreateSuccess(ToResponse(workOrder)));
    }

    /// <summary>Get one work order with its consumption snapshot.</summary>
    [HttpGet("work-orders/{workOrderId:int}")]
    [Authorize(Policy = CapabilityPolicies.View)]
    public async Task<IActionResult> GetWorkOrder(
        int companyId, int workOrderId, CancellationToken cancellationToken = default)
    {
        if (!await authorization.CanAccessCompanyAsync(User, companyId, CompanyCapability.View))
            return Forbid();

        var workOrder = await manufacturing.GetWorkOrderAsync(companyId, workOrderId, cancellationToken);
        return workOrder is null
            ? NotFound(ApiResponse<object>.CreateFailure("Work order not found in this company."))
            : Ok(ApiResponse<WorkOrderResponse>.CreateSuccess(ToResponse(workOrder)));
    }

    private string? RequireActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name) ? null : User.Identity!.Name;

    private static WorkOrderResponse ToResponse(WorkOrder workOrder) => new(
        workOrder.Id,
        workOrder.WorkOrderNumber,
        workOrder.ItemId,
        workOrder.Quantity,
        workOrder.LocationId,
        workOrder.Status.ToString(),
        workOrder.BomVersion,
        workOrder.ConsumedCost,
        workOrder.Consumptions.Select(consumption => new ConsumptionResponse(
            consumption.ComponentItemId, consumption.Quantity, consumption.ConsumedValue)).ToArray());
}

public sealed record BomResponse(
    int Id, int ItemId, int Version, bool IsActive, IReadOnlyList<BomLineResponse> Lines);

public sealed record BomLineResponse(int Id, int ComponentItemId, decimal QuantityPerUnit);

public sealed record WorkOrderResponse(
    int Id, string WorkOrderNumber, int ItemId, int Quantity, int LocationId, string Status,
    int BomVersion, decimal ConsumedCost, IReadOnlyList<ConsumptionResponse> Consumptions);

public sealed record ConsumptionResponse(int ComponentItemId, int Quantity, decimal ConsumedValue);
