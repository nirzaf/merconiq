using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Leads, opportunities and activities. CRM records forecast and history only; winning an opportunity links the
/// company's existing customer, and no CRM operation posts to the ledger or moves stock.
/// </summary>
public interface ICrmService
{
    /// <summary>Captures a new prospective customer.</summary>
    Task<CrmLead> CreateLeadAsync(int companyId, string name, string? email, string? phone, string source, string actor, CancellationToken cancellationToken = default);

    /// <summary>Marks a new lead as qualified and ready to convert.</summary>
    Task<CrmLead> QualifyLeadAsync(int companyId, int leadId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Disqualifies an unconverted lead with a mandatory reason.</summary>
    Task<CrmLead> DisqualifyLeadAsync(int companyId, int leadId, string reason, string actor, CancellationToken cancellationToken = default);

    /// <summary>Converts a qualified lead into an open opportunity in Prospecting.</summary>
    Task<CrmOpportunity> ConvertLeadAsync(int companyId, int leadId, string title, decimal expectedValue, DateOnly expectedCloseDate, string owner, string actor, CancellationToken cancellationToken = default);

    /// <summary>Creates an open opportunity directly, without a lead.</summary>
    Task<CrmOpportunity> CreateOpportunityAsync(int companyId, string title, decimal expectedValue, DateOnly expectedCloseDate, string owner, string actor, CancellationToken cancellationToken = default);

    /// <summary>Moves an open opportunity between Prospecting and Proposal; Won and Lost have their own actions.</summary>
    Task<CrmOpportunity> AdvanceOpportunityAsync(int companyId, int opportunityId, CrmOpportunityStage stage, string actor, CancellationToken cancellationToken = default);

    /// <summary>Wins an open opportunity against one of the company's existing customers.</summary>
    Task<CrmOpportunity> WinOpportunityAsync(int companyId, int opportunityId, int customerId, string actor, CancellationToken cancellationToken = default);

    /// <summary>Loses an open opportunity with a mandatory reason.</summary>
    Task<CrmOpportunity> LoseOpportunityAsync(int companyId, int opportunityId, string reason, string actor, CancellationToken cancellationToken = default);

    /// <summary>Logs one activity against exactly one lead or one opportunity.</summary>
    Task<CrmActivity> LogActivityAsync(int companyId, int? leadId, int? opportunityId, CrmActivityKind kind, string summary, DateTime occurredAtUtc, string actor, CancellationToken cancellationToken = default);

    /// <summary>Lists the company's leads, newest first.</summary>
    Task<IReadOnlyList<CrmLead>> ListLeadsAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Lists the company's opportunities, newest first.</summary>
    Task<IReadOnlyList<CrmOpportunity>> ListOpportunitiesAsync(int companyId, CancellationToken cancellationToken = default);

    /// <summary>Lists activities for one lead or one opportunity, oldest first.</summary>
    Task<IReadOnlyList<CrmActivity>> ListActivitiesAsync(int companyId, int? leadId, int? opportunityId, CancellationToken cancellationToken = default);

    /// <summary>Pipeline counts and expected value by stage.</summary>
    Task<CrmPipelineSummary> GetPipelineSummaryAsync(int companyId, CancellationToken cancellationToken = default);
}
