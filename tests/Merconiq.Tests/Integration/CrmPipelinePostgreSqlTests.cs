using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Relational acceptance for the CRM pipeline: lead qualification and conversion, win and loss rules that link the
/// company's own customer, exactly-one-target activities, pipeline totals, and company isolation. CRM never posts,
/// so these tests only assert pipeline state. Requires real PostgreSQL semantics and is skipped unless
/// RUN_POSTGRES_TESTS=true.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class CrmPipelinePostgreSqlTests
{
    private const string Actor = "crm-tester";

    private readonly PostgreSqlIntegrationFixture _fixture;

    public CrmPipelinePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task Lead_MustBeQualifiedBeforeItConvertsToAnOpportunity_AndConvertsOnce()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var lead = await WithCrmAsync(tenantId, crm => crm.CreateLeadAsync(companyId, "Harbor Foods", "buyer@harbor.example", null, "Trade show", Actor));

        var early = async () => await WithCrmAsync(tenantId, crm => crm.ConvertLeadAsync(
            companyId, lead.Id, "Harbor pilot", 5000m, Day(60), "sales-rep", Actor));
        await early.Should().ThrowAsync<FinancePostingConflictException>();

        await WithCrmAsync(tenantId, crm => crm.QualifyLeadAsync(companyId, lead.Id, Actor));
        var opportunity = await WithCrmAsync(tenantId, crm => crm.ConvertLeadAsync(
            companyId, lead.Id, "Harbor pilot", 5000m, Day(60), "sales-rep", Actor));

        opportunity.Stage.Should().Be(CrmOpportunityStage.Prospecting);
        opportunity.LeadId.Should().Be(lead.Id);
        var leads = await WithCrmAsync(tenantId, crm => crm.ListLeadsAsync(companyId));
        leads.Single().Status.Should().Be(CrmLeadStatus.Converted);

        var again = async () => await WithCrmAsync(tenantId, crm => crm.ConvertLeadAsync(
            companyId, lead.Id, "Duplicate", 1m, Day(60), "sales-rep", Actor));
        await again.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Disqualification_NeedsAReason_AndAConvertedLeadCannotBeDisqualified()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var lead = await WithCrmAsync(tenantId, crm => crm.CreateLeadAsync(companyId, "Cold list", null, null, "Import", Actor));

        var noReason = async () => await WithCrmAsync(tenantId, crm => crm.DisqualifyLeadAsync(companyId, lead.Id, "  ", Actor));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        var disqualified = await WithCrmAsync(tenantId, crm => crm.DisqualifyLeadAsync(companyId, lead.Id, "No budget this year", Actor));
        disqualified.Status.Should().Be(CrmLeadStatus.Disqualified);
        disqualified.DisqualifiedReason.Should().Be("No budget this year");
    }

    [PostgreSqlFact]
    public async Task Win_LinksAnExistingCustomer_AndClosesTheOpportunity()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var customerId = await CreateCustomerAsync(tenantId, companyId, "CUST-1");
        var opportunity = await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(
            companyId, "Annual supply", 12000m, Day(30), "sales-rep", Actor));
        await WithCrmAsync(tenantId, crm => crm.AdvanceOpportunityAsync(companyId, opportunity.Id, CrmOpportunityStage.Proposal, Actor));

        var won = await WithCrmAsync(tenantId, crm => crm.WinOpportunityAsync(companyId, opportunity.Id, customerId, Actor));

        won.Stage.Should().Be(CrmOpportunityStage.Won);
        won.CustomerId.Should().Be(customerId);
        won.ClosedAtUtc.Should().NotBeNull();

        var advance = async () => await WithCrmAsync(tenantId, crm => crm.AdvanceOpportunityAsync(companyId, opportunity.Id, CrmOpportunityStage.Proposal, Actor));
        await advance.Should().ThrowAsync<FinancePostingConflictException>();
        var lose = async () => await WithCrmAsync(tenantId, crm => crm.LoseOpportunityAsync(companyId, opportunity.Id, "Changed mind", Actor));
        await lose.Should().ThrowAsync<FinancePostingConflictException>();
    }

    [PostgreSqlFact]
    public async Task Win_RejectsForeignCustomers_AndLoss_NeedsAReasonAndIsFinal()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var otherCompanyId = await CreateCompanyAsync(tenantId, "OTHER");
        var foreignCustomer = await CreateCustomerAsync(tenantId, otherCompanyId, "FOREIGN-1");
        var opportunity = await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(
            companyId, "Pilot", 900m, Day(15), "sales-rep", Actor));

        var foreign = async () => await WithCrmAsync(tenantId, crm => crm.WinOpportunityAsync(companyId, opportunity.Id, foreignCustomer, Actor));
        (await foreign.Should().ThrowAsync<FinancePostingValidationException>()).WithMessage("*not found in this company*");

        var noReason = async () => await WithCrmAsync(tenantId, crm => crm.LoseOpportunityAsync(companyId, opportunity.Id, " ", Actor));
        await noReason.Should().ThrowAsync<FinancePostingValidationException>();

        var lost = await WithCrmAsync(tenantId, crm => crm.LoseOpportunityAsync(companyId, opportunity.Id, "Budget frozen", Actor));
        lost.Stage.Should().Be(CrmOpportunityStage.Lost);
        lost.LostReason.Should().Be("Budget frozen");

        var ownCustomer = await CreateCustomerAsync(tenantId, companyId, "OWN-1");
        var win = async () => await WithCrmAsync(tenantId, crm => crm.WinOpportunityAsync(companyId, opportunity.Id, ownCustomer, Actor));
        await win.Should().ThrowAsync<FinancePostingConflictException>("a lost opportunity is final");
    }

    [PostgreSqlFact]
    public async Task Activities_BelongToExactlyOneLeadOrOneOpportunity()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var lead = await WithCrmAsync(tenantId, crm => crm.CreateLeadAsync(companyId, "Mill Co", null, null, "Referral", Actor));
        var opportunity = await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(companyId, "Mill expansion", 300m, Day(20), "sales-rep", Actor));
        var when = new DateTime(DateTime.UtcNow.Year, 1, 5, 9, 0, 0, DateTimeKind.Utc);

        var none = async () => await WithCrmAsync(tenantId, crm => crm.LogActivityAsync(
            companyId, null, null, CrmActivityKind.Call, "Intro", when, Actor));
        await none.Should().ThrowAsync<FinancePostingValidationException>();

        var both = async () => await WithCrmAsync(tenantId, crm => crm.LogActivityAsync(
            companyId, lead.Id, opportunity.Id, CrmActivityKind.Call, "Intro", when, Actor));
        await both.Should().ThrowAsync<FinancePostingValidationException>();

        await WithCrmAsync(tenantId, crm => crm.LogActivityAsync(companyId, lead.Id, null, CrmActivityKind.Call, "Intro call", when, Actor));
        await WithCrmAsync(tenantId, crm => crm.LogActivityAsync(companyId, lead.Id, null, CrmActivityKind.Email, "Sent deck", when.AddDays(1), Actor));
        await WithCrmAsync(tenantId, crm => crm.LogActivityAsync(companyId, null, opportunity.Id, CrmActivityKind.Meeting, "Site visit", when.AddDays(2), Actor));

        var leadActivities = await WithCrmAsync(tenantId, crm => crm.ListActivitiesAsync(companyId, lead.Id, null));
        leadActivities.Select(activity => activity.Summary).Should().Equal("Intro call", "Sent deck");
        var opportunityActivities = await WithCrmAsync(tenantId, crm => crm.ListActivitiesAsync(companyId, null, opportunity.Id));
        opportunityActivities.Should().ContainSingle().Which.Kind.Should().Be(CrmActivityKind.Meeting);
    }

    [PostgreSqlFact]
    public async Task Pipeline_TotalsOpenValueAndExcludesClosedOpportunitiesFromOpenValue()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var customerId = await CreateCustomerAsync(tenantId, companyId, "CUST-P");
        var first = await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(companyId, "First", 100m, Day(10), "rep", Actor));
        var second = await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(companyId, "Second", 50m, Day(12), "rep", Actor));
        await WithCrmAsync(tenantId, crm => crm.AdvanceOpportunityAsync(companyId, second.Id, CrmOpportunityStage.Proposal, Actor));

        var before = await WithCrmAsync(tenantId, crm => crm.GetPipelineSummaryAsync(companyId));
        before.OpenExpectedValue.Should().Be(150m);

        await WithCrmAsync(tenantId, crm => crm.WinOpportunityAsync(companyId, first.Id, customerId, Actor));
        var after = await WithCrmAsync(tenantId, crm => crm.GetPipelineSummaryAsync(companyId));
        after.OpenExpectedValue.Should().Be(50m);
        after.Stages.Single(row => row.Stage == CrmOpportunityStage.Won).ExpectedValue.Should().Be(100m);
        after.Stages.Single(row => row.Stage == CrmOpportunityStage.Lost).Count.Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task Pipeline_IsInvisibleToOtherCompanies()
    {
        var tenantId = $"crm-{Guid.NewGuid():N}";
        var companyId = await CreateCompanyAsync(tenantId, "MAIN");
        var otherCompanyId = await CreateCompanyAsync(tenantId, "OTHER");
        await WithCrmAsync(tenantId, crm => crm.CreateOpportunityAsync(companyId, "Private", 10m, Day(5), "rep", Actor));

        var opportunities = await WithCrmAsync(tenantId, crm => crm.ListOpportunitiesAsync(otherCompanyId));
        opportunities.Should().BeEmpty();
        var summary = await WithCrmAsync(tenantId, crm => crm.GetPipelineSummaryAsync(otherCompanyId));
        summary.OpenExpectedValue.Should().Be(0m);
    }

    private static DateOnly Day(int dayOfMonthDelta) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(dayOfMonthDelta);

    private async Task<int> CreateCompanyAsync(string tenantId, string code)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var company = new Company { Code = code, LegalName = $"{code} Legal Name", BaseCurrency = "USD", IsActive = true };
        context.Companies.Add(company);
        await context.SaveChangesAsAsync(Actor);
        return company.Id;
    }

    private async Task<int> CreateCustomerAsync(string tenantId, int companyId, string code)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var customer = new Customer { CompanyId = companyId, CustomerCode = code, Name = $"Customer {code}", IsActive = true };
        context.Customers.Add(customer);
        await context.SaveChangesAsAsync(Actor);
        return customer.Id;
    }

    private async Task<T> WithCrmAsync<T>(string tenantId, Func<CrmService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        return await action(new CrmService(context, unitOfWork, NullLogger<CrmService>.Instance));
    }
}
