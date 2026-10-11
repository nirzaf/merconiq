using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Service tickets with priority-driven SLA resolution clocks (Critical 4h, High 24h,
/// Normal 72h). Breaches are surfaced by a read-only query; tickets never post.
/// </summary>
public sealed class TicketService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IDocumentNumberService documentNumbers,
    ILogger<TicketService> logger) : ITicketService
{
    private static TimeSpan ResolutionSla(TicketPriority priority) => priority switch
    {
        TicketPriority.Critical => TimeSpan.FromHours(4),
        TicketPriority.High => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(72)
    };

    /// <inheritdoc />
    public async Task<ServiceTicket> CreateTicketAsync(
        CreateTicketRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 200)
            throw new FinancePostingValidationException("A ticket subject of 1 to 200 characters is required.");

        ServiceTicket created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"tickets:{request.CompanyId}", cancellationToken);
            var now = DateTime.UtcNow;
            created = new ServiceTicket
            {
                CompanyId = request.CompanyId,
                Subject = request.Subject.Trim(),
                Description = request.Description,
                Priority = request.Priority,
                ProjectId = request.ProjectId,
                CreatedAtUtc = now,
                ResolutionDueAtUtc = now.Add(ResolutionSla(request.Priority))
            };
            if (request.ProjectId is { } projectId && !await context.Projects.AnyAsync(
                    project => project.Id == projectId && project.CompanyId == request.CompanyId,
                    cancellationToken))
                throw new FinancePostingValidationException("Project not found in this company.");

            context.ServiceTickets.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created.TicketNumber = await documentNumbers.AllocateAsync(
                request.CompanyId, "ServiceTicket", now.Year, "TKT", cancellationToken);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        logger.LogInformation("Ticket {TicketNumber} created in company {CompanyId}",
            created.TicketNumber, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<ServiceTicket> StartTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ServiceTicket started = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"tickets:{companyId}", cancellationToken);
            var ticket = await FindTicketAsync(companyId, ticketId, cancellationToken);
            if (ticket.Status is not (TicketStatus.Open or TicketStatus.InProgress))
                throw new FinancePostingConflictException(
                    $"A {ticket.Status} ticket cannot be started.");
            ticket.Status = TicketStatus.InProgress;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            started = ticket;
        }, cancellationToken: cancellationToken);
        return started;
    }

    /// <inheritdoc />
    public async Task<ServiceTicket> ResolveTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ServiceTicket resolved = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"tickets:{companyId}", cancellationToken);
            var ticket = await FindTicketAsync(companyId, ticketId, cancellationToken);
            if (ticket.Status is not (TicketStatus.Open or TicketStatus.InProgress))
                throw new FinancePostingConflictException(
                    $"A {ticket.Status} ticket cannot be resolved.");
            ticket.Status = TicketStatus.Resolved;
            ticket.ResolvedAtUtc = DateTime.UtcNow;
            ticket.ResolvedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            resolved = ticket;
        }, cancellationToken: cancellationToken);
        return resolved;
    }

    /// <inheritdoc />
    public async Task<ServiceTicket> CloseTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ServiceTicket closed = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"tickets:{companyId}", cancellationToken);
            var ticket = await FindTicketAsync(companyId, ticketId, cancellationToken);
            if (ticket.Status != TicketStatus.Resolved)
                throw new FinancePostingConflictException("Only resolved tickets can be closed.");
            ticket.Status = TicketStatus.Closed;
            ticket.ClosedAtUtc = DateTime.UtcNow;
            ticket.ClosedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            closed = ticket;
        }, cancellationToken: cancellationToken);
        return closed;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceTicket>> ListBreachedAsync(
        int companyId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var now = DateTime.UtcNow;
        var openOrInProgress = new[] { TicketStatus.Open, TicketStatus.InProgress };
        return await context.ServiceTickets
            .Where(ticket => ticket.CompanyId == companyId &&
                openOrInProgress.Contains(ticket.Status) &&
                ticket.ResolutionDueAtUtc != null &&
                ticket.ResolutionDueAtUtc < now)
            .OrderBy(ticket => ticket.ResolutionDueAtUtc)
            .ToListAsync(cancellationToken);
    }

    private Task<ServiceTicket> FindTicketAsync(
        int companyId, int ticketId, CancellationToken cancellationToken) =>
        context.ServiceTickets.SingleOrDefaultAsync(
            ticket => ticket.Id == ticketId && ticket.CompanyId == companyId, cancellationToken)
        ?? throw new FinancePostingValidationException("Ticket not found in this company.");
}
