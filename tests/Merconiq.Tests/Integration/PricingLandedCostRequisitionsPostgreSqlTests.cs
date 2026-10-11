using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Repositories;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Proves Phase 1 ERPNext parity capabilities against PostgreSQL:
/// 1. Price Lists and promotional/tiered Pricing Rules.
/// 2. Landed Cost Vouchers apportioning freight/import charges into inventory valuation and posting GL.
/// 3. Material Requests (Purchase Requisitions) and Supplier Quotation price comparison matrices.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class PricingLandedCostRequisitionsPostgreSqlTests
{
    private const string Actor = "parity-phase1-tester";
    private readonly PostgreSqlIntegrationFixture _fixture;

    public PricingLandedCostRequisitionsPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task PriceList_And_PricingRule_Resolves_CorrectTiers_And_Discounts()
    {
        var tenantId = $"pricing-{Guid.NewGuid():N}";
        int companyId;
        int customerId;
        int itemId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "PL-COMP", LegalName = "Pricing Corp", BaseCurrency = "USD", IsActive = true };
            var customer = new Customer { CustomerCode = "CUST-PL", Name = "VIP Customer", IsActive = true, Company = company };
            var item = new Item { ItemCode = "PROD-PL-1", Description = "Tiered Product", Rate = 30m, IsActive = true };
            setup.AddRange(company, customer, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            customerId = customer.Id;
            itemId = item.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var priceService = new PriceListService(context, NullLogger<PriceListService>.Instance);

        // 1. Create a Standard Selling Price List
        var priceList = await priceService.CreatePriceListAsync(
            new CreatePriceListRequest(companyId, "Wholesale USD", "USD", PriceListType.Selling),
            Actor);

        // 2. Set tiered item prices: 1-9 units @ $25, 10+ units @ $20
        await priceService.SetItemPriceAsync(
            new SetItemPriceRequest(priceList.Id, itemId, 25m, MinQuantity: 1m),
            Actor);
        await priceService.SetItemPriceAsync(
            new SetItemPriceRequest(priceList.Id, itemId, 20m, MinQuantity: 10m),
            Actor);

        // Resolve for 5 units -> expects $25
        var r5 = await priceService.ResolveRateAsync(
            new ResolveRateRequest(companyId, itemId, PriceListType.Selling, Currency: "USD", Quantity: 5m, PartyId: customerId));
        r5.BaseRate.Should().Be(25m);
        r5.FinalRate.Should().Be(25m);
        r5.AppliedPriceListName.Should().Be("Wholesale USD");

        // Resolve for 12 units -> expects $20
        var r12 = await priceService.ResolveRateAsync(
            new ResolveRateRequest(companyId, itemId, PriceListType.Selling, Currency: "USD", Quantity: 12m, PartyId: customerId));
        r12.BaseRate.Should().Be(20m);
        r12.FinalRate.Should().Be(20m);

        // 3. Create a Promotional Pricing Rule: 10% discount for this customer
        await priceService.CreatePricingRuleAsync(
            new CreatePricingRuleRequest(
                companyId,
                "VIP 10% Off",
                PriceListType.Selling,
                ItemId: itemId,
                PartyId: customerId,
                DiscountPercentage: 10m,
                Priority: 2),
            Actor);

        // Resolve for 12 units again -> base $20, 10% discount = $2 discount, final rate $18
        var rVip = await priceService.ResolveRateAsync(
            new ResolveRateRequest(companyId, itemId, PriceListType.Selling, Currency: "USD", Quantity: 12m, PartyId: customerId));
        rVip.BaseRate.Should().Be(20m);
        rVip.DiscountPercentage.Should().Be(10m);
        rVip.DiscountAmount.Should().Be(2m);
        rVip.FinalRate.Should().Be(18m);
        rVip.AppliedPricingRuleTitle.Should().Be("VIP 10% Off");
    }

    [PostgreSqlFact]
    public async Task LandedCostVoucher_ApportionsCharges_IncreasesMovingAverageCost_AndPostsGL()
    {
        var tenantId = $"lcv-{Guid.NewGuid():N}";
        int companyId;
        int supplierId;
        int locationId;
        int itemId;
        int purchaseOrderId;
        int purchaseOrderLineId;
        int freightAccountId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "LCV-COMP", LegalName = "Landed Cost Corp", BaseCurrency = "USD", IsActive = true };
            var branch = new Branch { Code = "LCV-BR", Name = "Branch 1", TimeZoneId = "UTC", Company = company };
            var location = new Location { Name = "Main Warehouse", Branch = branch };
            var supplier = new Supplier { Name = "Import Supplier", IsDeleted = false };
            var item = new Item { ItemCode = "IMPORT-ITEM", Description = "Imported Widget", Rate = 10m, IsActive = true };

            setup.AddRange(company, branch, location, supplier, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            supplierId = supplier.Id;
            locationId = location.Id;
            itemId = item.Id;

            var accounts = new[]
            {
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1000", Name = "Bank", AccountType = "Asset", IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "1300", Name = "Inventory", AccountType = "Asset", IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "2100", Name = "GRNI", AccountType = "Liability", IsActive = true },
                new ChartOfAccount { CompanyId = companyId, AccountCode = "5100", Name = "Freight & Customs Expense", AccountType = "Expense", IsActive = true }
            };
            setup.ChartOfAccounts.AddRange(accounts);
            await setup.SaveChangesAsAsync(Actor);

            freightAccountId = accounts.Single(a => a.AccountCode == "5100").Id;
            var inventoryAccountId = accounts.Single(a => a.AccountCode == "1300").Id;

            setup.CompanyAccountMappings.AddRange(
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Bank, ChartOfAccountId = accounts.Single(a => a.AccountCode == "1000").Id, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.Inventory, ChartOfAccountId = inventoryAccountId, IsActive = true },
                new CompanyAccountMapping { CompanyId = companyId, Role = AccountPostingRole.GoodsReceivedNotInvoiced, ChartOfAccountId = accounts.Single(a => a.AccountCode == "2100").Id, IsActive = true });
            await setup.SaveChangesAsAsync(Actor);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var calendar = new FiscalCalendarService(setup, new UnitOfWork(setup), NullLogger<FiscalCalendarService>.Instance);
            await calendar.CreateFiscalYearAsync(
                new CreateFiscalYearRequest(companyId, $"FY{today.Year}", new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
                Actor);

            var po = new PurchaseOrder
            {
                SupplierId = supplierId,
                PONumber = "PO-LCV-001",
                OrderDate = DateTime.UtcNow,
                CurrencyScale = 2,
                CalculationVersion = 1,
                CommercialVersion = 1,
                ApprovedCommercialVersion = 1,
                ApprovedCommercialSnapshotJson = "{}",
                TotalAmount = 1000m,
                NetAmount = 1000m,
                Status = PurchaseOrderStatus.Approved,
                OrderDetails =
                [
                    new OrderDetail
                    {
                        ItemId = itemId,
                        Quantity = 100,
                        UnitPrice = 10m,
                        CurrencyScale = 2,
                        CalculationVersion = 1,
                        NetAmount = 1000m,
                        GrossAmount = 1000m
                    }
                ]
            };
            setup.PurchaseOrders.Add(po);
            var detail = po.OrderDetails.Single();
            var doc = DocumentIdentity.Create(po.DocumentId, tenantId, companyId, "PurchaseOrder", po.PONumber, today.Year, DocumentLifecycleStatus.Active, Actor);
            var docLine = DocumentLineIdentity.Create(detail.DocumentLineId, doc.Id, tenantId, companyId, "PurchaseOrderLine");
            setup.DocumentIdentities.Add(doc);
            setup.DocumentLineIdentities.Add(docLine);
            await setup.SaveChangesAsAsync(Actor);

            purchaseOrderId = po.Id;
            purchaseOrderLineId = detail.Id;
        }

        // 1. Post Goods Receipt for 100 units @ $10 -> Inventory 1000 / GRNI 1000
        int goodsReceiptId;
        await using (var grContext = _fixture.CreateContext(tenantId))
        {
            var uow = new UnitOfWork(grContext);
            var webhookMock = new Mock<IWebhookDispatcher>();
            var stockService = new StockService(
                new Repository<StockInHand>(grContext),
                new Repository<StockTransaction>(grContext),
                new Repository<Item>(grContext),
                new Repository<Location>(grContext),
                new Repository<Branch>(grContext),
                uow,
                webhookMock.Object,
                new TestTenantContext(tenantId),
                NullLogger<StockService>.Instance,
                new Repository<StockValuationBucket>(grContext),
                new Repository<StockValuationEntry>(grContext));
            var fiscalCal = new FiscalCalendarService(grContext, uow, NullLogger<FiscalCalendarService>.Instance);
            var docNum = new DocumentNumberService(grContext, uow);
            var posting = new JournalPostingService(grContext, uow, fiscalCal, docNum, NullLogger<JournalPostingService>.Instance);
            var grService = new GoodsReceiptService(
                grContext, uow, stockService, posting, new AccountPostingPolicyService(grContext), fiscalCal, docNum, NullLogger<GoodsReceiptService>.Instance);

            var grResult = await grService.PostGoodsReceiptAsync(
                new PostGoodsReceiptRequest(
                    companyId, purchaseOrderId, locationId, "lcv-gr-1",
                    [new PostGoodsReceiptLine(purchaseOrderLineId, 100)]),
                new StockMutationScope(companyId, () => Task.FromResult(true)),
                Actor);

            goodsReceiptId = grResult.GoodsReceiptId;
        }

        // Verify initial valuation: 100 units at value 1000 (unit cost = 10)
        await using (var verifyInitial = _fixture.CreateContext(tenantId))
        {
            var bucket = await verifyInitial.StockValuationBuckets
                .SingleAsync(b => b.ItemId == itemId && b.LocationId == locationId);
            bucket.Quantity.Should().Be(100);
            bucket.Value.Should().Be(1000m);
        }

        // 2. Create and Post Landed Cost Voucher adding $200 freight
        await using (var lcvContext = _fixture.CreateContext(tenantId))
        {
            var uow = new UnitOfWork(lcvContext);
            var fiscalCal = new FiscalCalendarService(lcvContext, uow, NullLogger<FiscalCalendarService>.Instance);
            var docNum = new DocumentNumberService(lcvContext, uow);
            var posting = new JournalPostingService(lcvContext, uow, fiscalCal, docNum, NullLogger<JournalPostingService>.Instance);
            var policy = new AccountPostingPolicyService(lcvContext);
            var lcvService = new LandedCostService(lcvContext, posting, policy, NullLogger<LandedCostService>.Instance);

            var voucher = await lcvService.CreateVoucherAsync(
                new CreateLandedCostVoucherRequest(
                    companyId,
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    [goodsReceiptId],
                    [new LandedCostChargeInput(freightAccountId, "Ocean freight & customs import duty", 200m)],
                    "Import charges from port"),
                Actor);

            var result = await lcvService.PostLandedCostVoucherAsync(voucher.Id, Actor);
            result.Status.Should().Be(LandedCostStatus.Submitted);
            result.TotalAdditionalCharges.Should().Be(200m);
            result.JournalNumber.Should().StartWith("JV");

            var itemSummary = result.ItemAllocations.Single();
            itemSummary.InitialCost.Should().Be(10m);
            itemSummary.AdditionalCharge.Should().Be(200m);
            itemSummary.FinalValuationCost.Should().Be(12m, "1000 initial value + 200 landed charges = 1200 / 100 units = 12 per unit");
        }

        // 3. Verify StockValuationBucket and GL Trial Balance
        await using (var verifyFinal = _fixture.CreateContext(tenantId))
        {
            var bucket = await verifyFinal.StockValuationBuckets
                .SingleAsync(b => b.ItemId == itemId && b.LocationId == locationId);
            bucket.Quantity.Should().Be(100, "Quantity must not change");
            bucket.Value.Should().Be(1200m, "1000 stock value + 200 landed cost added");

            var uow = new UnitOfWork(verifyFinal);
            var fiscalCal = new FiscalCalendarService(verifyFinal, uow, NullLogger<FiscalCalendarService>.Instance);
            var docNum = new DocumentNumberService(verifyFinal, uow);
            var posting = new JournalPostingService(verifyFinal, uow, fiscalCal, docNum, NullLogger<JournalPostingService>.Instance);
            var tb = await posting.GetTrialBalanceAsync(companyId, null, null);

            tb.TotalDebit.Should().Be(tb.TotalCredit, "GL kernel invariant holds");
            var inventoryRow = tb.Rows.Single(r => r.AccountCode == "1300");
            inventoryRow.Debit.Should().Be(1200m, "1000 GR receipt + 200 landed cost capitalization");

            var freightRow = tb.Rows.Single(r => r.AccountCode == "5100");
            freightRow.Credit.Should().Be(200m, "Freight expense clearing credit");
        }
    }

    [PostgreSqlFact]
    public async Task MaterialRequest_And_SupplierQuotation_Comparison_Works()
    {
        var tenantId = $"req-{Guid.NewGuid():N}";
        int companyId;
        int itemId;
        int supplier1Id;
        int supplier2Id;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "REQ-COMP", LegalName = "Requisition Corp", BaseCurrency = "USD", IsActive = true };
            var sup1 = new Supplier { Name = "Global Tech Supplies", IsDeleted = false };
            var sup2 = new Supplier { Name = "Local Discount Vendor", IsDeleted = false };
            var item = new Item { ItemCode = "OFFICE-LAPTOP", Description = "Developer Laptop", Rate = 1500m, IsActive = true };

            setup.AddRange(company, sup1, sup2, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            supplier1Id = sup1.Id;
            supplier2Id = sup2.Id;
            itemId = item.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var reqService = new ProcurementRequisitionService(context, NullLogger<ProcurementRequisitionService>.Instance);

        // 1. Material Request lifecycle
        var mr = await reqService.CreateMaterialRequestAsync(
            new CreateMaterialRequest(
                companyId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14),
                MaterialRequestPurpose.Purchase,
                "IT Department",
                [new MaterialRequestLineInput(itemId, 5)],
                "Laptops for new engineering hires"),
            Actor);

        mr.Status.Should().Be(MaterialRequestStatus.Draft);
        mr.TotalItemCount.Should().Be(1);

        var approvedMr = await reqService.ApproveMaterialRequestAsync(companyId, mr.MaterialRequestId, Actor);
        approvedMr.Status.Should().Be(MaterialRequestStatus.Approved);

        // 2. Capture Supplier Quotations for price comparison
        await reqService.CreateSupplierQuotationAsync(
            new CreateSupplierQuotationRequest(
                companyId,
                supplier1Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                "USD",
                [new SupplierQuotationLineInput(itemId, 5, 1400m)]),
            Actor);

        await reqService.CreateSupplierQuotationAsync(
            new CreateSupplierQuotationRequest(
                companyId,
                supplier2Id,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
                "USD",
                [new SupplierQuotationLineInput(itemId, 5, 1250m)]),
            Actor);

        // 3. Compare quotations
        var comparison = await reqService.CompareSupplierQuotationsAsync(companyId, itemId);
        comparison.Quotes.Should().HaveCount(2);
        comparison.Quotes[0].SupplierName.Should().Be("Local Discount Vendor");
        comparison.Quotes[0].Rate.Should().Be(1250m, "Ranked lowest cost first");
        comparison.Quotes[1].SupplierName.Should().Be("Global Tech Supplies");
        comparison.Quotes[1].Rate.Should().Be(1400m);
    }
}
