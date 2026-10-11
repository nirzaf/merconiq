using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class ShopFloorService(
    InventoryDbContext context,
    ILogger<ShopFloorService> logger) : IShopFloorService
{
    public async Task<Workstation> CreateWorkstationAsync(
        CreateWorkstationRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || string.IsNullOrWhiteSpace(request.Name))
            throw new FinancePostingValidationException("CompanyId and Name are required.");

        var ws = new Workstation
        {
            CompanyId = request.CompanyId,
            Name = request.Name.Trim(),
            HourlyRate = request.HourlyRate,
            OperatingCapacityHours = request.CapacityHours > 0 ? request.CapacityHours : 8m,
            IsActive = true
        };

        context.Workstations.Add(ws);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return ws;
    }

    public async Task<RoutingOperation> CreateRoutingOperationAsync(
        CreateRoutingOperationRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.WorkstationId <= 0 || string.IsNullOrWhiteSpace(request.OperationName))
            throw new FinancePostingValidationException("CompanyId, WorkstationId, and OperationName are required.");

        var op = new RoutingOperation
        {
            CompanyId = request.CompanyId,
            OperationName = request.OperationName.Trim(),
            WorkstationId = request.WorkstationId,
            StandardDurationMinutes = request.StandardDurationMinutes > 0 ? request.StandardDurationMinutes : 60,
            CostPerHour = request.CostPerHour
        };

        context.RoutingOperations.Add(op);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return op;
    }

    public async Task<JobCard> CreateJobCardAsync(
        CreateJobCardRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.WorkOrderId <= 0 || request.RoutingOperationId <= 0)
            throw new FinancePostingValidationException("CompanyId, WorkOrderId, and RoutingOperationId are required.");

        var jc = new JobCard
        {
            CompanyId = request.CompanyId,
            WorkOrderId = request.WorkOrderId,
            RoutingOperationId = request.RoutingOperationId,
            AssignedTechnician = request.Technician?.Trim() ?? string.Empty,
            Status = JobCardStatus.Pending
        };

        context.JobCards.Add(jc);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return jc;
    }

    public async Task<JobCard> StartJobCardAsync(
        int jobCardId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var jc = await context.JobCards.FindAsync([jobCardId], cancellationToken)
            ?? throw new FinancePostingValidationException("JobCard not found.");

        if (jc.Status != JobCardStatus.Pending)
            throw new FinancePostingValidationException($"Cannot start JobCard in {jc.Status} state.");

        jc.Status = JobCardStatus.InProgress;
        jc.StartedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return jc;
    }

    public async Task<JobCard> CompleteJobCardAsync(
        CompleteJobCardRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var jc = await context.JobCards
            .Include(j => j.RoutingOperation)
            .FirstOrDefaultAsync(j => j.Id == request.JobCardId, cancellationToken)
            ?? throw new FinancePostingValidationException("JobCard not found.");

        if (jc.Status != JobCardStatus.InProgress && jc.Status != JobCardStatus.Pending)
            throw new FinancePostingValidationException($"Cannot complete JobCard in {jc.Status} state.");

        jc.Status = JobCardStatus.Completed;
        jc.CompletedAtUtc = DateTime.UtcNow;
        jc.ActualDurationMinutes = request.ActualMinutes > 0 ? request.ActualMinutes : jc.RoutingOperation.StandardDurationMinutes;

        var hours = jc.ActualDurationMinutes / 60m;
        jc.TotalCost = Math.Round(hours * jc.RoutingOperation.CostPerHour, 4);

        await context.SaveChangesAsAsync(actor, cancellationToken);
        return jc;
    }
}
