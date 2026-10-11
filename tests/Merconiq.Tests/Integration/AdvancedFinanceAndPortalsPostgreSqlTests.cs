using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Repositories;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class AdvancedFinanceAndPortalsPostgreSqlTests
{
    private const string Actor = "adv-finance-portal-tester";
    private readonly PostgreSqlIntegrationFixture _fixture;

    public AdvancedFinanceAndPortalsPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task ForeignCurrency_SetsRates_ConvertsAmounts_RunsAndReversesRevaluation()
    {
        var tenantId = $"fx-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId);

        // 1. Set exchange rate
        var rateDate = new DateOnly(2026, 10, 1);
        var rateView = await WithFxServiceAsync(tenantId, s => s.SetExchangeRateAsync(
            new SetExchangeRateRequest(env.CompanyId, "EUR", "USD", rateDate, 1.10m)));

        rateView.Rate.Should().Be(1.10m);
        rateView.FromCurrency.Should().Be("EUR");
        rateView.ToCurrency.Should().Be("USD");

        // 2. Convert amount
        var converted = await WithFxServiceAsync(tenantId, s => s.ConvertAmountAsync(
            new ConvertCurrencyRequest(env.CompanyId, 100m, "EUR", "USD", rateDate)));

        converted.ConvertedAmount.Should().Be(110.00m);
        converted.AppliedRate.Should().Be(1.10m);

        // 3. Realized Gain/Loss
        var realized = await WithFxServiceAsync(tenantId, s => Task.FromResult(
            s.CalculateRealizedGainLoss(100m, 110m)));
        realized.Should().Be(10.00m, "110 settled vs 100 original gives 10 gain");

        // 4. Unrealized Revaluation run
        var revalResult = await WithFxServiceAsync(tenantId, s => s.RunUnrealizedRevaluationAsync(
            new RunFxRevaluationRequest(
                env.CompanyId,
                rateDate,
                env.FiscalPeriodId,
                env.UnrealizedGainAccountId,
                env.UnrealizedLossAccountId)));

        revalResult.TotalGain.Should().BeGreaterThan(0);
        revalResult.Status.Should().Be("Posted");
        revalResult.JournalEntryId.Should().NotBeNull();

        // 5. Reverse revaluation
        await WithFxServiceAsync(tenantId, async s =>
        {
            await s.ReverseUnrealizedRevaluationAsync(revalResult.RunId);
            return true;
        });

        await using var verify = _fixture.CreateContext(tenantId);
        var run = await verify.FxRevaluationRuns.SingleAsync(r => r.Id == revalResult.RunId);
        run.Status.Should().Be(FxRevaluationStatus.Reversed);
        run.ReversalJournalEntryId.Should().NotBeNull();
    }

    [PostgreSqlFact]
    public async Task Intercompany_ConfiguresRelationship_PostsReciprocalTx_EliminationReportBalances()
    {
        var tenantId = $"ic-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId, withSubsidiary: true);

        // 1. Configure relationship
        var rel = await WithIntercompanyServiceAsync(tenantId, s => s.ConfigureRelationshipAsync(
            new ConfigureIntercompanyRelationshipRequest(
                env.CompanyId,
                env.SubsidiaryCompanyId,
                env.AccountsReceivableId,
                env.SalesRevenueId,
                env.AccountsPayableId,
                env.OperatingExpenseId)));

        rel.IsActive.Should().BeTrue();
        rel.ParentCompanyId.Should().Be(env.CompanyId);
        rel.SubsidiaryCompanyId.Should().Be(env.SubsidiaryCompanyId);

        // 2. Post reciprocal intercompany transaction
        var refKey = $"IC-REF-{Guid.NewGuid():N}";
        var tx = await WithIntercompanyServiceAsync(tenantId, s => s.PostTransactionAsync(
            new PostIntercompanyTransactionRequest(
                rel.Id,
                env.CompanyId,
                env.SubsidiaryCompanyId,
                "SalesInvoice",
                101,
                refKey,
                1500m,
                "USD",
                "Management fees billing")));

        tx.Status.Should().Be("Reconciled");
        tx.Amount.Should().Be(1500m);

        // 3. Duplicate reference key is rejected
        var duplicate = () => WithIntercompanyServiceAsync(tenantId, s => s.PostTransactionAsync(
            new PostIntercompanyTransactionRequest(
                rel.Id,
                env.CompanyId,
                env.SubsidiaryCompanyId,
                "SalesInvoice",
                102,
                refKey,
                1500m,
                "USD",
                "Duplicate billing")));

        await duplicate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");

        // 4. Elimination report
        var report = await WithIntercompanyServiceAsync(tenantId, s => s.GetEliminationReportAsync(
            env.CompanyId, env.SubsidiaryCompanyId));

        report.ParentReceivableTotal.Should().Be(1500m);
        report.SubsidiaryPayableTotal.Should().Be(1500m);
        report.Variance.Should().Be(0);
        report.IsBalanced.Should().BeTrue();
        report.Transactions.Should().HaveCount(1);
    }

    [PostgreSqlFact]
    public async Task StatutoryTax_CountryProfiles_GeneratesZatcaSha256Hash_ProducesTaxReturnSummary()
    {
        var tenantId = $"tax-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId);

        // 1. Country profile lookup
        var profile = await WithStatutoryTaxServiceAsync(tenantId, s => Task.FromResult(s.GetCountryTaxProfile("SA")));
        profile.StandardVatRate.Should().Be(0.15m);
        profile.RequiresCryptographicHash.Should().BeTrue();
        profile.CurrencyCode.Should().Be("SAR");

        // Seed a sample sales invoice and line
        int invoiceId;
        await using (var seed = _fixture.CreateContext(tenantId))
        {
            var project = new Project
            {
                CompanyId = env.CompanyId,
                Code = "PRJ-STAT-1",
                Name = "Statutory Tax Project",
                CustomerId = env.CustomerId,
                Status = ProjectStatus.Active
            };
            var task = new ProjectTask
            {
                CompanyId = env.CompanyId,
                Project = project,
                Name = "Auditing",
                Status = ProjectTaskStatus.Open
            };
            var timesheet = new TimesheetEntry
            {
                CompanyId = env.CompanyId,
                Project = project,
                ProjectTask = task,
                WorkerId = "auditor",
                WorkDate = new DateOnly(2026, 10, 5),
                Hours = 10,
                Billable = true,
                HourlyRate = 100m,
                Status = TimesheetEntryStatus.Approved
            };
            seed.AddRange(project, task, timesheet);
            await seed.SaveChangesAsync();

            var invoice = new SalesInvoice
            {
                CompanyId = env.CompanyId,
                CustomerId = env.CustomerId,
                InvoiceNumber = "INV-STAT-001",
                InvoiceDate = new DateOnly(2026, 10, 5),
                DueDate = new DateOnly(2026, 10, 20),
                PostingDate = new DateOnly(2026, 10, 5),
                TotalValue = 1000m,
                PostedAtUtc = DateTime.UtcNow,
                PostedBy = Actor,
                RequestKey = "req-stat-1",
                RequestHash = "hash-stat-1"
            };
            invoice.Lines.Add(new SalesInvoiceLine
            {
                CompanyId = env.CompanyId,
                LineNumber = 1,
                Quantity = 10,
                RevenueValue = 1000m,
                TimesheetEntryId = timesheet.Id
            });
            seed.SalesInvoices.Add(invoice);
            await seed.SaveChangesAsync();
            invoiceId = invoice.Id;
        }

        // 2. Generate e-invoice export with SHA-256 cryptographic hash
        var eInvoice = await WithStatutoryTaxServiceAsync(tenantId, s => s.GenerateStatutoryInvoiceExportAsync(
            env.CompanyId, invoiceId, "SA"));

        eInvoice.InvoiceNumber.Should().Be("INV-STAT-001");
        eInvoice.Subtotal.Should().Be(1000m);
        eInvoice.TaxAmount.Should().Be(150m, "15% on 1000");
        eInvoice.TotalAmount.Should().Be(1150m);
        eInvoice.CryptographicHash.Should().NotBeNullOrEmpty();
        eInvoice.CryptographicHash.Length.Should().Be(64, "SHA-256 hex string is 64 chars");
        eInvoice.Lines.Should().HaveCount(1);
        eInvoice.TaxCategories.Should().ContainSingle(c => c.CategoryName == "Standard Rate" && c.TaxAmount == 150m);

        // 3. Tax return summary
        var summary = await WithStatutoryTaxServiceAsync(tenantId, s => s.GenerateTaxReturnSummaryAsync(
            env.CompanyId, env.FiscalPeriodId));

        summary.StandardRateSales.Should().Be(1000m);
        summary.StandardOutputTax.Should().Be(150m);
        summary.TotalOutputTax.Should().Be(150m);
        summary.NetTaxPayable.Should().Be(150m);
    }

    [PostgreSqlFact]
    public async Task Portals_CustomerPortal_ScopesToCustomer_RejectsCrossCustomer()
    {
        var tenantId = $"portal-c-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId);

        // Register customer contact
        var customerEmail = "contact@customer.com";
        await using (var seed = _fixture.CreateContext(tenantId))
        {
            seed.PortalContacts.Add(new PortalContact
            {
                TenantId = tenantId,
                CompanyId = env.CompanyId,
                PortalType = PortalType.Customer,
                PartyId = env.CustomerId,
                Email = customerEmail,
                ContactName = "John Doe",
                IsActive = true
            });
            await seed.SaveChangesAsync();
        }

        // Customer accesses their overview
        var overview = await WithPortalServiceAsync(tenantId, s => s.GetCustomerPortalOverviewAsync(
            env.CompanyId, env.CustomerId, customerEmail));

        overview.CustomerId.Should().Be(env.CustomerId);
        overview.CustomerName.Should().Be("Parity customer");

        // Unauthorized contact rejected
        var unauthorized = () => WithPortalServiceAsync(tenantId, s => s.GetCustomerPortalOverviewAsync(
            env.CompanyId, env.CustomerId, "stranger@other.com"));
        await unauthorized.Should().ThrowAsync<UnauthorizedAccessException>();

        // Cross-customer access rejected
        var crossCustomer = () => WithPortalServiceAsync(tenantId, s => s.GetCustomerPortalOverviewAsync(
            env.CompanyId, 99999, customerEmail));
        await crossCustomer.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [PostgreSqlFact]
    public async Task Portals_SupplierPortal_AcknowledgesOrder_SubmitsInvoiceDraft_BuyerReviews()
    {
        var tenantId = $"portal-s-{Guid.NewGuid():N}";
        var env = await CreateEnvironmentAsync(tenantId);

        var supplierEmail = "contact@supplier.com";
        int poId;
        await using (var seed = _fixture.CreateContext(tenantId))
        {
            var supplier = new Supplier
            {
                Name = "Apex Supplier",
                IsDeleted = false
            };
            seed.Suppliers.Add(supplier);
            await seed.SaveChangesAsync();

            seed.PortalContacts.Add(new PortalContact
            {
                TenantId = tenantId,
                CompanyId = env.CompanyId,
                PortalType = PortalType.Supplier,
                PartyId = supplier.Id,
                Email = supplierEmail,
                ContactName = "Jane Supplier",
                IsActive = true
            });

            var po = new PurchaseOrder
            {
                SupplierId = supplier.Id,
                PONumber = "PO-2026-0001",
                OrderDate = DateTime.UtcNow,
                TotalAmount = 2500m,
                Status = PurchaseOrderStatus.Approved,
                CommercialVersion = 1,
                ApprovedCommercialVersion = 1,
                ApprovedCommercialSnapshotJson = "{}"
            };
            var docId = DocumentIdentity.Create(
                po.DocumentId,
                tenantId,
                env.CompanyId,
                "PurchaseOrder",
                "PO-2026-0001",
                2026,
                DocumentLifecycleStatus.Active,
                "PurchaseOrder.Create");
            po.DocumentIdentity = docId;
            seed.DocumentIdentities.Add(docId);
            seed.PurchaseOrders.Add(po);
            await seed.SaveChangesAsync();

            env = env with { SupplierId = supplier.Id };
            poId = po.Id;
        }

        // 1. Supplier overview
        var overview = await WithPortalServiceAsync(tenantId, s => s.GetSupplierPortalOverviewAsync(
            env.CompanyId, env.SupplierId, supplierEmail));

        overview.SupplierId.Should().Be(env.SupplierId);
        overview.Orders.Should().HaveCount(1);
        overview.Orders[0].OrderNumber.Should().Be("PO-2026-0001");

        // 2. Supplier acknowledges order
        var ack = await WithPortalServiceAsync(tenantId, s => s.SubmitSupplierAcknowledgmentAsync(
            new SubmitSupplierAcknowledgmentRequest(
                env.SupplierId, poId, "SUP-ACK-100", new DateOnly(2026, 10, 25)),
            supplierEmail));

        ack.Status.Should().Be("PendingBuyerReview");
        ack.SupplierReference.Should().Be("SUP-ACK-100");

        // 3. Supplier submits invoice reference (unposted draft proposal)
        var invSub = await WithPortalServiceAsync(tenantId, s => s.SubmitSupplierInvoiceReferenceAsync(
            new SubmitSupplierInvoiceReferenceRequest(
                env.SupplierId, poId, "SUP-INV-999", 2500m),
            supplierEmail));

        invSub.Status.Should().Be("PendingBuyerReview");
        invSub.InvoiceReferenceNumber.Should().Be("SUP-INV-999");

        // 4. Buyer reviews and accepts submission
        var reviewed = await WithPortalServiceAsync(tenantId, s => s.ReviewSupplierSubmissionAsync(
            new ReviewSupplierSubmissionRequest(invSub.Id, Approve: true, ReviewNotes: "Approved for AP voucher processing")));

        reviewed.Status.Should().Be("Accepted");
        reviewed.BuyerReviewNotes.Should().Be("Approved for AP voucher processing");
    }

    private async Task<T> WithFxServiceAsync<T>(string tenantId, Func<ForeignCurrencyService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var unitOfWork = new UnitOfWork(context);
        var fiscalCalendar = new FiscalCalendarService(context, unitOfWork, NullLogger<FiscalCalendarService>.Instance);
        var docNumber = new DocumentNumberService(context, unitOfWork);
        var posting = new JournalPostingService(context, unitOfWork, fiscalCalendar, docNumber, NullLogger<JournalPostingService>.Instance);
        var service = new ForeignCurrencyService(context, posting, new TestTenantContext(tenantId));
        return await action(service);
    }

    private async Task<T> WithIntercompanyServiceAsync<T>(string tenantId, Func<IntercompanyService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var service = new IntercompanyService(context, new TestTenantContext(tenantId));
        return await action(service);
    }

    private async Task<T> WithStatutoryTaxServiceAsync<T>(string tenantId, Func<StatutoryTaxService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var service = new StatutoryTaxService(context, new TestTenantContext(tenantId));
        return await action(service);
    }

    private async Task<T> WithPortalServiceAsync<T>(string tenantId, Func<PortalService, Task<T>> action)
    {
        await using var context = _fixture.CreateContext(tenantId);
        var service = new PortalService(context, new TestTenantContext(tenantId));
        return await action(service);
    }

    private async Task<TestFinanceEnv> CreateEnvironmentAsync(string tenantId, bool withSubsidiary = false)
    {
        await using var setup = _fixture.CreateContext(tenantId);
        var company = new Company
        {
            Code = "MC-MAIN", LegalName = "Merconiq Main Corp", BaseCurrency = "USD", CurrencyScale = 2, IsActive = true
        };
        var customer = new Customer
        {
            CustomerCode = "CUST-001", Name = "Parity customer", IsActive = true
        };
        customer.Company = company;
        setup.AddRange(company, customer);
        await setup.SaveChangesAsync();

        int subsidiaryId = 0;
        if (withSubsidiary)
        {
            var sub = new Company
            {
                Code = "MC-SUB", LegalName = "Merconiq Subsidiary Ltd", BaseCurrency = "USD", CurrencyScale = 2, IsActive = true
            };
            setup.Companies.Add(sub);
            await setup.SaveChangesAsync();
            subsidiaryId = sub.Id;
        }

        // Accounts
        var accounts = new[]
        {
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "1000", Name = "Bank", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "1100", Name = "Accounts Receivable", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "2000", Name = "Accounts Payable", AccountType = "Liability", AccountClass = AccountClass.Liability, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "4000", Name = "Sales Revenue", AccountType = "Income", AccountClass = AccountClass.Income, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "5000", Name = "Operating Expense", AccountType = "Expense", AccountClass = AccountClass.Expense, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "7010", Name = "Unrealized FX Gain", AccountType = "Income", AccountClass = AccountClass.Income, IsActive = true },
            new ChartOfAccount { CompanyId = company.Id, AccountCode = "7020", Name = "Unrealized FX Loss", AccountType = "Expense", AccountClass = AccountClass.Expense, IsActive = true }
        };
        setup.ChartOfAccounts.AddRange(accounts);
        await setup.SaveChangesAsync();

        // Fiscal period
        var fiscalYear = new FiscalYear
        {
            CompanyId = company.Id, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31)
        };
        setup.FiscalYears.Add(fiscalYear);
        await setup.SaveChangesAsync();

        var period = new FiscalPeriod
        {
            FiscalYearId = fiscalYear.Id,
            CompanyId = company.Id,
            PeriodNumber = 10,
            Name = "October 2026",
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = FiscalPeriodStatus.Open
        };
        setup.FiscalPeriods.Add(period);
        await setup.SaveChangesAsync();

        return new TestFinanceEnv(
            company.Id,
            subsidiaryId,
            customer.Id,
            0,
            period.Id,
            accounts.Single(a => a.AccountCode == "1100").Id,
            accounts.Single(a => a.AccountCode == "2000").Id,
            accounts.Single(a => a.AccountCode == "4000").Id,
            accounts.Single(a => a.AccountCode == "5000").Id,
            accounts.Single(a => a.AccountCode == "7010").Id,
            accounts.Single(a => a.AccountCode == "7020").Id);
    }

    private sealed record TestFinanceEnv(
        int CompanyId,
        int SubsidiaryCompanyId,
        int CustomerId,
        int SupplierId,
        int FiscalPeriodId,
        int AccountsReceivableId,
        int AccountsPayableId,
        int SalesRevenueId,
        int OperatingExpenseId,
        int UnrealizedGainAccountId,
        int UnrealizedLossAccountId);
}
