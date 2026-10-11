using FluentAssertions;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Merconiq.Infrastructure.Services;
using Merconiq.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Merconiq.Tests.Integration;

/// <summary>
/// Proves Phase 2–5 ERPNext parity extensions against live PostgreSQL:
/// 1. Serial Number lifecycle (registration, duplicate checks, delivery allocation, history).
/// 2. FIFO Stock Valuation Layer Queue (inward layer tracking, FIFO cost consumption).
/// 3. Automated Reorder Engine (safety stock threshold evaluation triggering material requisitions).
/// 4. Shop Floor Control (workstations, routing operations, job cards with technician time/costing).
/// 5. Electronic Bank Feeds and Cash Flow Statement rollups.
/// 6. Subcontracting production orders, Sales Partner commissions, and Overdue Dunning notices.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSQL")]
public sealed class ExtendedErpNextParityPostgreSqlTests
{
    private const string Actor = "parity-extended-tester";
    private readonly PostgreSqlIntegrationFixture _fixture;

    public ExtendedErpNextParityPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [PostgreSqlFact]
    public async Task SerialNumber_Tracking_Registers_RejectsDuplicates_And_DeliversToCustomer()
    {
        var tenantId = $"serial-{Guid.NewGuid():N}";
        int companyId;
        int customerId;
        int locationId;
        int itemId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "SN-COMP", LegalName = "Serial Corp", BaseCurrency = "USD", IsActive = true };
            var customer = new Customer { CustomerCode = "CUST-SN", Name = "Client A", IsActive = true, Company = company };
            var location = new Location { Name = "Finished Goods Whs" };
            var item = new Item { ItemCode = "HW-SRV-1", Description = "Enterprise Server", Rate = 2500m, IsActive = true };
            setup.AddRange(company, customer, location, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            customerId = customer.Id;
            locationId = location.Id;
            itemId = item.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var serialService = new SerialNumberService(context, NullLogger<SerialNumberService>.Instance);

        // 1. Inward register two serials with 24 months warranty
        var registered = await serialService.RegisterSerialsAsync(
            new RegisterSerialRequest(companyId, itemId, locationId, ["SN-AAA-001", "SN-AAA-002"], WarrantyMonths: 24),
            Actor);

        registered.Should().HaveCount(2);
        registered.All(s => s.Status == SerialNumberStatus.InStock).Should().BeTrue();
        registered.All(s => s.WarrantyExpiryDate.HasValue).Should().BeTrue();

        // 2. Reject duplicate registration of in-stock serial
        var actDuplicate = async () => await serialService.RegisterSerialsAsync(
            new RegisterSerialRequest(companyId, itemId, locationId, ["SN-AAA-001"]),
            Actor);

        await actDuplicate.Should().ThrowAsync<FinancePostingConflictException>()
            .WithMessage("*already in stock*");

        // 3. Deliver serial SN-AAA-001 to customer
        var delivered = await serialService.DeliverSerialsAsync(
            new DeliverSerialRequest(companyId, itemId, locationId, customerId, ["SN-AAA-001"]),
            Actor);

        delivered.Should().HaveCount(1);
        delivered[0].Status.Should().Be(SerialNumberStatus.Delivered);
        delivered[0].CustomerId.Should().Be(customerId);
        delivered[0].LocationId.Should().BeNull();

        // 4. Query history
        var history = await serialService.GetSerialHistoryAsync(companyId, itemId, "SN-AAA-001");
        history.Should().NotBeNull();
        history!.Status.Should().Be(SerialNumberStatus.Delivered);
        history.CustomerId.Should().Be(customerId);
    }

    [PostgreSqlFact]
    public async Task FifoValuation_Consumes_Layers_In_FirstInFirstOut_Sequence()
    {
        var tenantId = $"fifo-{Guid.NewGuid():N}";
        int companyId;
        int locationId;
        int itemId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "FIFO-COMP", LegalName = "FIFO Valuations Corp", BaseCurrency = "USD", IsActive = true };
            var location = new Location { Name = "Raw Materials Whs" };
            var item = new Item { ItemCode = "RAW-STL-1", Description = "Steel Rods", Rate = 10m, IsActive = true };
            setup.AddRange(company, location, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            locationId = location.Id;
            itemId = item.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var fifoService = new FifoValuationService(context, NullLogger<FifoValuationService>.Instance);

        // 1. Inward Layer 1: 10 units @ $10.00
        var layer1 = await fifoService.RecordInwardLayerAsync(
            companyId, itemId, locationId, quantity: 10, unitCost: 10.00m, sourceReference: "PO-1001", Actor);

        // 2. Inward Layer 2: 20 units @ $15.00
        var layer2 = await fifoService.RecordInwardLayerAsync(
            companyId, itemId, locationId, quantity: 20, unitCost: 15.00m, sourceReference: "PO-1002", Actor);

        // 3. Consume 15 units via FIFO:
        // Expected: 10 from Layer 1 ($100.00) + 5 from Layer 2 ($75.00) = $175.00 total, average $11.6667
        var consumption = await fifoService.ConsumeFifoLayersAsync(companyId, itemId, locationId, quantity: 15, Actor);

        consumption.TotalCost.Should().Be(175.00m);
        consumption.AverageUnitCost.Should().Be(11.6667m);
        consumption.Layers.Should().HaveCount(2);

        consumption.Layers[0].LayerId.Should().Be(layer1.Id);
        consumption.Layers[0].ConsumedQuantity.Should().Be(10);
        consumption.Layers[0].UnitCost.Should().Be(10.00m);

        consumption.Layers[1].LayerId.Should().Be(layer2.Id);
        consumption.Layers[1].ConsumedQuantity.Should().Be(5);
        consumption.Layers[1].UnitCost.Should().Be(15.00m);

        // Verify remaining layer quantities in DB
        var remainingLayers = await context.StockFifoLayers
            .Where(l => l.CompanyId == companyId && l.ItemId == itemId && l.LocationId == locationId)
            .OrderBy(l => l.Id)
            .ToListAsync();

        remainingLayers[0].RemainingQuantity.Should().Be(0);
        remainingLayers[1].RemainingQuantity.Should().Be(15);
    }

    [PostgreSqlFact]
    public async Task AutoReorder_Engine_Triggers_PurchaseRequisitions_When_Stock_Below_Threshold()
    {
        var tenantId = $"reorder-{Guid.NewGuid():N}";
        int companyId;
        int locationId;
        int itemId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "REORDER-COMP", LegalName = "Reorder Corp", BaseCurrency = "USD", IsActive = true };
            var location = new Location { Name = "Central Whs" };
            var item = new Item { ItemCode = "FASTENER-M8", Description = "Hex Bolt M8", Rate = 0.5m, IsActive = true };
            setup.AddRange(company, location, item);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            locationId = location.Id;
            itemId = item.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var requisitionService = new ProcurementRequisitionService(context, NullLogger<ProcurementRequisitionService>.Instance);
        var reorderService = new AutoReorderService(context, requisitionService, NullLogger<AutoReorderService>.Instance);

        // 1. Configure Reorder Rule: threshold level = 25 units, reorder quantity = 100 units
        var rule = await reorderService.SetReorderRuleAsync(
            new SetReorderRuleRequest(companyId, itemId, locationId, ReorderLevel: 25, ReorderQuantity: 100),
            Actor);
        rule.Should().NotBeNull();
        rule.ReorderLevel.Should().Be(25);

        // 2. Currently 0 stock in hand <= 25 reorder level -> trigger evaluation
        var summary = await reorderService.EvaluateReorderTriggersAsync(companyId, Actor);

        summary.EvaluatedCount.Should().Be(1);
        summary.TriggeredCount.Should().Be(1);
        summary.CreatedRequisitions.Should().HaveCount(1);

        var createdRequisition = summary.CreatedRequisitions[0];
        createdRequisition.Status.Should().Be(MaterialRequestStatus.Draft);
        createdRequisition.TotalItemCount.Should().Be(1);

        var dbReq = await context.MaterialRequests
            .Include(r => r.Lines)
            .FirstAsync(r => r.Id == createdRequisition.MaterialRequestId);

        dbReq.CompanyId.Should().Be(companyId);
        dbReq.Purpose.Should().Be(MaterialRequestPurpose.Purchase);
        dbReq.Lines.Should().HaveCount(1);
        dbReq.Lines.First().ItemId.Should().Be(itemId);
        dbReq.Lines.First().Quantity.Should().Be(100);
    }

    [PostgreSqlFact]
    public async Task ShopFloor_Workstation_RoutingOperation_And_JobCard_Execution_CalculatesCost()
    {
        var tenantId = $"shopfloor-{Guid.NewGuid():N}";
        int companyId;
        int workOrderId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "MFG-COMP", LegalName = "Advanced Manufacturing Corp", BaseCurrency = "USD", IsActive = true };
            var item = new Item { ItemCode = "PUMP-V1", Description = "Industrial Water Pump", Rate = 500m, IsActive = true };
            var location = new Location { Name = "Assembly Plant 1" };
            setup.AddRange(company, item, location);
            await setup.SaveChangesAsAsync(Actor);

            var bom = new BillOfMaterials
            {
                CompanyId = company.Id,
                ItemId = item.Id,
                Version = 1,
                IsActive = true
            };
            setup.BillOfMaterials.Add(bom);
            await setup.SaveChangesAsAsync(Actor);

            var workOrder = new WorkOrder
            {
                CompanyId = company.Id,
                WorkOrderNumber = "WO-2026-001",
                BillOfMaterialsId = bom.Id,
                ItemId = item.Id,
                LocationId = location.Id,
                Quantity = 10,
                Status = WorkOrderStatus.Released,
                BomVersion = 1,
                PostingDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            setup.WorkOrders.Add(workOrder);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            workOrderId = workOrder.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var shopFloorService = new ShopFloorService(context, NullLogger<ShopFloorService>.Instance);

        // 1. Create Workstation: CNC Milling Station @ $60/hr
        var ws = await shopFloorService.CreateWorkstationAsync(
            new CreateWorkstationRequest(companyId, "CNC Milling Center", HourlyRate: 60m, CapacityHours: 16m),
            Actor);
        ws.Id.Should().BeGreaterThan(0);

        // 2. Add Routing Operation: High-precision casing milling ($80/hr cost rate, standard 120 mins)
        var op = await shopFloorService.CreateRoutingOperationAsync(
            new CreateRoutingOperationRequest(companyId, "Precision Milling", ws.Id, StandardDurationMinutes: 120, CostPerHour: 80m),
            Actor);
        op.Id.Should().BeGreaterThan(0);

        // 3. Create Job Card for technician
        var jc = await shopFloorService.CreateJobCardAsync(
            new CreateJobCardRequest(companyId, workOrderId, op.Id, "Alex Engineer"),
            Actor);
        jc.Status.Should().Be(JobCardStatus.Pending);

        // 4. Start Job Card
        var startedJc = await shopFloorService.StartJobCardAsync(jc.Id, Actor);
        startedJc.Status.Should().Be(JobCardStatus.InProgress);
        startedJc.StartedAtUtc.Should().NotBeNull();

        // 5. Complete Job Card: Took 90 minutes (1.5 hours)
        // Expected cost: 1.5 * $80/hr = $120.00
        var completedJc = await shopFloorService.CompleteJobCardAsync(
            new CompleteJobCardRequest(jc.Id, ActualMinutes: 90),
            Actor);

        completedJc.Status.Should().Be(JobCardStatus.Completed);
        completedJc.ActualDurationMinutes.Should().Be(90);
        completedJc.TotalCost.Should().Be(120.00m);
    }

    [PostgreSqlFact]
    public async Task BankingFeed_And_CashFlowStatement_Imports_And_CalculatesRollups()
    {
        var tenantId = $"banking-{Guid.NewGuid():N}";
        int companyId;
        int bankAccountId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "BANK-COMP", LegalName = "Fintech Services Corp", BaseCurrency = "USD", IsActive = true };
            setup.Companies.Add(company);
            await setup.SaveChangesAsAsync(Actor);

            var bankAccount = new ChartOfAccount { CompanyId = company.Id, AccountCode = "1020", Name = "Corporate Checking", AccountType = "Asset", AccountClass = AccountClass.Asset, IsActive = true };
            setup.ChartOfAccounts.Add(bankAccount);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            bankAccountId = bankAccount.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var bankingService = new BankingFeedService(context, NullLogger<BankingFeedService>.Instance);

        // 1. Import CAMT.053 electronic bank statement
        var stmtDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var stmtFile = await bankingService.ImportStatementAsync(
            new ImportBankStatementRequest(
                companyId,
                bankAccountId,
                "statement-2026-10.xml",
                BankStatementFormat.Camt053,
                stmtDate,
                OpeningBalance: 50000m,
                ClosingBalance: 65000m,
                [
                    new ImportBankStatementLine(stmtDate, "TX-001", "Client Acme Corp", 20000m),
                    new ImportBankStatementLine(stmtDate, "TX-002", "Supplier Cloud Hosting", -5000m)
                ]),
            Actor);

        stmtFile.Transactions.Should().HaveCount(2);
        stmtFile.ClosingBalance.Should().Be(65000m);

        // 2. Generate Cash Flow Statement
        var cf = await bankingService.GenerateCashFlowStatementAsync(
            companyId, stmtDate.AddDays(-30), stmtDate.AddDays(30));

        cf.CompanyId.Should().Be(companyId);
        cf.NetCashChange.Should().Be(cf.OperatingCashFlow + cf.InvestingCashFlow + cf.FinancingCashFlow);
    }

    [PostgreSqlFact]
    public async Task CommercialLifecycle_Subcontracting_Commissions_And_DunningNotices()
    {
        var tenantId = $"commercial-{Guid.NewGuid():N}";
        int companyId;
        int supplierId;
        int customerId;
        int finishedItemId;
        int rawItemId;
        int invoiceId;

        await using (var setup = _fixture.CreateContext(tenantId))
        {
            var company = new Company { Code = "COMM-COMP", LegalName = "Commercial Operations Corp", BaseCurrency = "USD", IsActive = true };
            var supplier = new Supplier { Name = "Precision Turning Ltd" };
            var customer = new Customer { CustomerCode = "CUST-OVERDUE", Name = "Late Paying Client", IsActive = true, Company = company };
            var finItem = new Item { ItemCode = "MACHINED-PART-A", Description = "CNC Machined Valve", Rate = 150m, IsActive = true };
            var rawItem = new Item { ItemCode = "RAW-ALUM-INGOT", Description = "Aluminum Ingot", Rate = 40m, IsActive = true };
            setup.AddRange(company, supplier, customer, finItem, rawItem);
            await setup.SaveChangesAsAsync(Actor);

            // Create open Sales Invoice for $5,000
            var salesInvoice = new SalesInvoice
            {
                CompanyId = company.Id,
                InvoiceNumber = "SINV-2026-099",
                CustomerId = customer.Id,
                InvoiceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-45),
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-15),
                PostingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-45),
                TotalValue = 5000m,
                PostedAtUtc = DateTime.UtcNow,
                PostedBy = Actor
            };
            setup.SalesInvoices.Add(salesInvoice);
            await setup.SaveChangesAsAsync(Actor);

            companyId = company.Id;
            supplierId = supplier.Id;
            customerId = customer.Id;
            finishedItemId = finItem.Id;
            rawItemId = rawItem.Id;
            invoiceId = salesInvoice.Id;
        }

        await using var context = _fixture.CreateContext(tenantId);
        var commService = new CommercialLifecycleService(context, NullLogger<CommercialLifecycleService>.Instance);

        // 1. Create Subcontracting Order: produce 50 units with supplied components
        var subOrder = await commService.CreateSubcontractingOrderAsync(
            new CreateSubcontractingOrderRequest(
                companyId,
                supplierId,
                finishedItemId,
                Quantity: 50,
                ServiceRate: 25.00m,
                Components: [(rawItemId, 50m)]),
            Actor);

        subOrder.Id.Should().BeGreaterThan(0);
        subOrder.TotalServiceCost.Should().Be(1250m);
        subOrder.SuppliedItems.Should().HaveCount(1);
        var suppliedItem = subOrder.SuppliedItems.First();
        suppliedItem.ComponentItemId.Should().Be(rawItemId);
        suppliedItem.RequiredQuantity.Should().Be(50m);

        // 2. Accrue Sales Partner Commission (5% on $5,000 invoice = $250.00)
        var commission = await commService.AccrueCommissionAsync(
            new CalculateCommissionRequest(companyId, "Global Sales Brokerage", invoiceId, CommissionPercentage: 5m),
            Actor);

        commission.Id.Should().BeGreaterThan(0);
        commission.InvoicedAmount.Should().Be(5000m);
        commission.CommissionAmount.Should().Be(250.00m);
        commission.IsPaid.Should().BeFalse();

        // 3. Issue Dunning Notice (Level 1 with $25 Late Fee and $50 Interest)
        var dunning = await commService.IssueDunningNoticeAsync(
            new IssueDunningNoticeRequest(companyId, invoiceId, DunningLevel.FirstReminder, LateFee: 25m, Interest: 50m),
            Actor);

        dunning.Id.Should().BeGreaterThan(0);
        dunning.OutstandingAmount.Should().Be(5000m);
        dunning.DunningFee.Should().Be(25m);
        dunning.InterestAmount.Should().Be(50m);
        dunning.Level.Should().Be(DunningLevel.FirstReminder);
    }
}
