using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Lead and opportunity pipeline. Every state change runs under the company's CRM lock and re-reads the record
/// inside it, so a lead cannot be converted twice and an opportunity cannot be both won and lost.
/// </summary>
public sealed class CrmService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<CrmService> logger) : ICrmService
{
    private const int MaximumTextLength = 500;
    private const int MaximumNameLength = 200;

    /// <inheritdoc />
    public async Task<CrmLead> CreateLeadAsync(
        int companyId, string name, string? email, string? phone, string source, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var leadName = RequireText(name, MaximumNameLength, "A lead name of 1 to 200 characters");
        var leadSource = RequireText(source, 100, "A lead source of 1 to 100 characters");
        var lead = new CrmLead
        {
            CompanyId = companyId,
            Name = leadName,
            Email = TrimOrNull(email, 254),
            Phone = TrimOrNull(phone, 40),
            Source = leadSource,
            Status = CrmLeadStatus.New,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = actor.Trim()
        };

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await RequireCompanyAsync(companyId, cancellationToken);
            context.CrmLeads.Add(lead);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        logger.LogInformation("Captured CRM lead {LeadId} for company {CompanyId}", lead.Id, companyId);
        return lead;
    }

    /// <inheritdoc />
    public async Task<CrmLead> QualifyLeadAsync(int companyId, int leadId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        CrmLead result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var lead = await FindLeadAsync(companyId, leadId, cancellationToken);
            if (lead.Status != CrmLeadStatus.New)
                throw new FinancePostingConflictException($"Lead '{lead.Name}' is {lead.Status.ToString().ToLowerInvariant()} and cannot be qualified.");
            lead.Status = CrmLeadStatus.Qualified;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = lead;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmLead> DisqualifyLeadAsync(int companyId, int leadId, string reason, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var reasonText = RequireText(reason, MaximumTextLength, "A disqualification reason of 1 to 500 characters");
        CrmLead result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var lead = await FindLeadAsync(companyId, leadId, cancellationToken);
            if (lead.Status is CrmLeadStatus.Converted or CrmLeadStatus.Disqualified)
                throw new FinancePostingConflictException($"Lead '{lead.Name}' is {lead.Status.ToString().ToLowerInvariant()} and cannot be disqualified.");
            lead.Status = CrmLeadStatus.Disqualified;
            lead.DisqualifiedReason = reasonText;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = lead;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmOpportunity> ConvertLeadAsync(
        int companyId, int leadId, string title, decimal expectedValue, DateOnly expectedCloseDate, string owner, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var opportunityTitle = RequireText(title, MaximumNameLength, "An opportunity title of 1 to 200 characters");
        var ownerName = RequireText(owner, 256, "An opportunity owner of 1 to 256 characters");
        RequireExpectedValue(expectedValue);

        CrmOpportunity result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var lead = await FindLeadAsync(companyId, leadId, cancellationToken);
            if (lead.Status != CrmLeadStatus.Qualified)
                throw new FinancePostingConflictException(
                    $"Lead '{lead.Name}' is {lead.Status.ToString().ToLowerInvariant()}; only a qualified lead can be converted.");

            var opportunity = new CrmOpportunity
            {
                CompanyId = companyId,
                Title = opportunityTitle,
                LeadId = lead.Id,
                Stage = CrmOpportunityStage.Prospecting,
                ExpectedValue = expectedValue,
                ExpectedCloseDate = expectedCloseDate,
                Owner = ownerName,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUser = actor.Trim()
            };
            lead.Status = CrmLeadStatus.Converted;
            context.CrmOpportunities.Add(opportunity);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = opportunity;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmOpportunity> CreateOpportunityAsync(
        int companyId, string title, decimal expectedValue, DateOnly expectedCloseDate, string owner, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var opportunityTitle = RequireText(title, MaximumNameLength, "An opportunity title of 1 to 200 characters");
        var ownerName = RequireText(owner, 256, "An opportunity owner of 1 to 256 characters");
        RequireExpectedValue(expectedValue);

        var opportunity = new CrmOpportunity
        {
            CompanyId = companyId,
            Title = opportunityTitle,
            Stage = CrmOpportunityStage.Prospecting,
            ExpectedValue = expectedValue,
            ExpectedCloseDate = expectedCloseDate,
            Owner = ownerName,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = actor.Trim()
        };
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await RequireCompanyAsync(companyId, cancellationToken);
            context.CrmOpportunities.Add(opportunity);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return opportunity;
    }

    /// <inheritdoc />
    public async Task<CrmOpportunity> AdvanceOpportunityAsync(
        int companyId, int opportunityId, CrmOpportunityStage stage, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        if (stage is CrmOpportunityStage.Won or CrmOpportunityStage.Lost)
            throw new FinancePostingValidationException("Use the win or lose action to close an opportunity.");
        CrmOpportunity result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var opportunity = await FindOpenOpportunityAsync(companyId, opportunityId, cancellationToken);
            opportunity.Stage = stage;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = opportunity;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmOpportunity> WinOpportunityAsync(
        int companyId, int opportunityId, int customerId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        CrmOpportunity result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var customer = await context.Customers.SingleOrDefaultAsync(
                    candidate => candidate.Id == customerId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Customer not found in this company.");
            if (!customer.IsActive)
                throw new FinancePostingValidationException($"Customer '{customer.CustomerCode}' is inactive and cannot be won against.");
            var opportunity = await FindOpenOpportunityAsync(companyId, opportunityId, cancellationToken);
            opportunity.Stage = CrmOpportunityStage.Won;
            opportunity.CustomerId = customer.Id;
            opportunity.ClosedAtUtc = DateTime.UtcNow;
            opportunity.ClosedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = opportunity;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmOpportunity> LoseOpportunityAsync(
        int companyId, int opportunityId, string reason, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var reasonText = RequireText(reason, MaximumTextLength, "A lost reason of 1 to 500 characters");
        CrmOpportunity result = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"crm:{companyId}", cancellationToken);
            var opportunity = await FindOpenOpportunityAsync(companyId, opportunityId, cancellationToken);
            opportunity.Stage = CrmOpportunityStage.Lost;
            opportunity.LostReason = reasonText;
            opportunity.ClosedAtUtc = DateTime.UtcNow;
            opportunity.ClosedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            result = opportunity;
        }, cancellationToken: cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CrmActivity> LogActivityAsync(
        int companyId, int? leadId, int? opportunityId, CrmActivityKind kind, string summary, DateTime occurredAtUtc, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        if (leadId.HasValue == opportunityId.HasValue)
            throw new FinancePostingValidationException("An activity belongs to exactly one lead or one opportunity.");
        if (!Enum.IsDefined(kind))
            throw new FinancePostingValidationException("The activity kind is not recognized.");
        var text = RequireText(summary, MaximumTextLength, "An activity summary of 1 to 500 characters");

        var activity = new CrmActivity
        {
            CompanyId = companyId,
            LeadId = leadId,
            OpportunityId = opportunityId,
            Kind = kind,
            Summary = text,
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            RecordedByUser = actor.Trim()
        };
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (leadId is int lead)
                await FindLeadAsync(companyId, lead, cancellationToken);
            if (opportunityId is int opportunity)
                await FindOpportunityAsync(companyId, opportunity, cancellationToken);
            context.CrmActivities.Add(activity);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return activity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrmLead>> ListLeadsAsync(int companyId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.CrmLeads
            .Where(lead => lead.CompanyId == companyId)
            .OrderByDescending(lead => lead.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrmOpportunity>> ListOpportunitiesAsync(int companyId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.CrmOpportunities
            .Where(opportunity => opportunity.CompanyId == companyId)
            .OrderByDescending(opportunity => opportunity.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrmActivity>> ListActivitiesAsync(
        int companyId, int? leadId, int? opportunityId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        if (leadId.HasValue == opportunityId.HasValue)
            throw new FinancePostingValidationException("Filter activities by exactly one lead or one opportunity.");
        var query = context.CrmActivities.Where(activity => activity.CompanyId == companyId);
        query = leadId is int lead
            ? query.Where(activity => activity.LeadId == lead)
            : query.Where(activity => activity.OpportunityId == opportunityId);
        return await query
            .OrderBy(activity => activity.OccurredAtUtc)
            .ThenBy(activity => activity.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CrmPipelineSummary> GetPipelineSummaryAsync(int companyId, CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var totals = await context.CrmOpportunities
            .Where(opportunity => opportunity.CompanyId == companyId)
            .GroupBy(opportunity => opportunity.Stage)
            .Select(group => new { Stage = group.Key, Count = group.Count(), Value = group.Sum(opportunity => opportunity.ExpectedValue) })
            .ToListAsync(cancellationToken);
        var stages = Enum.GetValues<CrmOpportunityStage>()
            .Select(stage =>
            {
                var row = totals.SingleOrDefault(total => total.Stage == stage);
                return new CrmPipelineStageRow(stage, row?.Count ?? 0, row?.Value ?? 0m);
            })
            .ToList();
        var open = stages
            .Where(row => row.Stage is CrmOpportunityStage.Prospecting or CrmOpportunityStage.Proposal)
            .Sum(row => row.ExpectedValue);
        return new CrmPipelineSummary(stages, open);
    }

    private async Task RequireCompanyAsync(int companyId, CancellationToken cancellationToken)
    {
        var exists = await context.Companies.AnyAsync(company => company.Id == companyId && company.IsActive, cancellationToken);
        if (!exists)
            throw new FinancePostingValidationException("Active company not found.");
    }

    private async Task<CrmLead> FindLeadAsync(int companyId, int leadId, CancellationToken cancellationToken) =>
        await context.CrmLeads.SingleOrDefaultAsync(
            candidate => candidate.Id == leadId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Lead not found in this company.");

    private async Task<CrmOpportunity> FindOpportunityAsync(int companyId, int opportunityId, CancellationToken cancellationToken) =>
        await context.CrmOpportunities.SingleOrDefaultAsync(
            candidate => candidate.Id == opportunityId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Opportunity not found in this company.");

    private async Task<CrmOpportunity> FindOpenOpportunityAsync(int companyId, int opportunityId, CancellationToken cancellationToken)
    {
        var opportunity = await FindOpportunityAsync(companyId, opportunityId, cancellationToken);
        if (opportunity.Stage is CrmOpportunityStage.Won or CrmOpportunityStage.Lost)
            throw new FinancePostingConflictException(
                $"Opportunity '{opportunity.Title}' is {opportunity.Stage.ToString().ToLowerInvariant()} and cannot change stage.");
        return opportunity;
    }

    private static void RequireExpectedValue(decimal expectedValue)
    {
        if (expectedValue < 0m)
            throw new FinancePostingValidationException("The expected value cannot be negative.");
    }

    private static void RequirePositiveCompany(int companyId)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
    }

    private static string RequireText(string? value, int maximumLength, string requirement)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 || trimmed.Length > maximumLength)
            throw new FinancePostingValidationException($"{requirement} is required.");
        return trimmed;
    }

    private static string? TrimOrNull(string? value, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > maximumLength)
            throw new FinancePostingValidationException($"A value may be at most {maximumLength} characters.");
        return trimmed;
    }
}
