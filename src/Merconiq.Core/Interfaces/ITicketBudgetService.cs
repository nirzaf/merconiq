using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>Service tickets with priority-driven SLA clocks; breaches are reported, never hidden.</summary>
public interface ITicketService
{
    Task<ServiceTicket> CreateTicketAsync(
        CreateTicketRequest request, string actor, CancellationToken cancellationToken = default);

    Task<ServiceTicket> StartTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default);

    Task<ServiceTicket> ResolveTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default);

    Task<ServiceTicket> CloseTicketAsync(
        int companyId, int ticketId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Open tickets whose SLA resolution clock has already passed.</summary>
    Task<IReadOnlyList<ServiceTicket>> ListBreachedAsync(
        int companyId, CancellationToken cancellationToken = default);
}

/// <summary>Sets one account's annual budget and reports plan versus posted actuals.</summary>
public interface IBudgetService
{
    Task<Budget> SetBudgetAsync(
        SetBudgetRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Budget versus posted ledger actuals for one account and year.</summary>
    Task<BudgetVersusActual> GetBudgetVersusActualAsync(
        int companyId, int budgetId, CancellationToken cancellationToken = default);
}
