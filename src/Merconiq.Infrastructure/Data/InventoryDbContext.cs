using Merconiq.Core.Entities;
using Merconiq.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Merconiq.Infrastructure.Data;

/// <summary>
/// Entity Framework Core context for the inventory system. Extends <see cref="IdentityDbContext{TUser}"/>
/// and additionally handles <see cref="AuditableEntity"/> timestamps, soft-delete translation,
/// and a two-phase audit log pipeline that captures old / new / changed values for every save.
/// </summary>
public class InventoryDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    public InventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ITenantContext tenantContext,
        IHttpContextAccessor? httpContextAccessor = null) : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Tenant selected for this request or explicitly-created work scope.</summary>
    public string CurrentTenantId => _tenantContext.TenantId;

    /// <summary>Catalog of items.</summary>
    public DbSet<Item> Items { get; set; } = null!;
    public DbSet<UnitOfMeasure> UnitsOfMeasure { get; set; } = null!;

    /// <summary>Current stock-in-hand per item per location.</summary>
    public DbSet<StockInHand> StockInHand { get; set; } = null!;
    public DbSet<StockCount> StockCounts { get; set; } = null!;
    public DbSet<StockCountLine> StockCountLines { get; set; } = null!;
    public DbSet<StockCountObservation> StockCountObservations { get; set; } = null!;
    public DbSet<StockCountVariance> StockCountVariances { get; set; } = null!;

    /// <summary>Tenant-scoped stock reservations.</summary>
    public DbSet<StockReservation> StockReservations { get; set; } = null!;
    public DbSet<TransferOrder> TransferOrders { get; set; } = null!;
    public DbSet<TransferOrderLine> TransferOrderLines { get; set; } = null!;
    public DbSet<TransferTransitEntry> TransferTransitEntries { get; set; } = null!;
    public DbSet<TransferTransitSettlement> TransferTransitSettlements { get; set; } = null!;

    /// <summary>Lot-specific allocations belonging to source-line stock reservations.</summary>
    public DbSet<StockReservationAllocation> StockReservationAllocations { get; set; } = null!;

    /// <summary>Current weighted-average stock valuation per item and location.</summary>
    public DbSet<StockValuationBucket> StockValuationBuckets { get; set; } = null!;

    /// <summary>Append-only valuation postings linked to stock movements.</summary>
    public DbSet<StockValuationEntry> StockValuationEntries { get; set; } = null!;

    /// <summary>Storage locations.</summary>
    public DbSet<Location> Locations { get; set; } = null!;

    /// <summary>Suppliers of items.</summary>
    public DbSet<Supplier> Suppliers { get; set; } = null!;

    /// <summary>Company-scoped customer master records.</summary>
    public DbSet<Customer> Customers { get; set; } = null!;

    /// <summary>Company-owned chart-of-account configuration records.</summary>
    public DbSet<ChartOfAccount> ChartOfAccounts { get; set; } = null!;

    /// <summary>Purchase orders.</summary>
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;

    /// <summary>Purchase order line items.</summary>
    public DbSet<OrderDetail> OrderDetails { get; set; } = null!;

    /// <summary>Effective-dated tenant tax policies.</summary>
    public DbSet<TaxRule> TaxRules { get; set; } = null!;

    /// <summary>Historical stock movements (receive, transfer, sell).</summary>
    public DbSet<StockTransaction> StockTransactions { get; set; } = null!;

    /// <summary>Append-only audit log.</summary>
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    /// <summary>Webhook subscriptions for outbound event notifications.</summary>
    public DbSet<WebhookSubscription> WebhookSubscriptions { get; set; } = null!;

    /// <summary>Durable outbound webhook delivery attempts.</summary>
    public DbSet<WebhookDelivery> WebhookDeliveries { get; set; } = null!;

    /// <summary>Durable API idempotency claims and results.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; } = null!;

    /// <summary>Approved opening-stock replay records.</summary>
    public DbSet<OpeningStockImport> OpeningStockImports { get; set; } = null!;
    public DbSet<OpeningStockImportLine> OpeningStockImportLines { get; set; } = null!;
    public DbSet<OpeningStockCorrection> OpeningStockCorrections { get; set; } = null!;

    /// <summary>Tenant-owned legal and trading companies.</summary>
    public DbSet<Company> Companies { get; set; } = null!;

    /// <summary>Company-owned operating branches.</summary>
    public DbSet<Branch> Branches { get; set; } = null!;
    /// <summary>Company-scoped user capability grants.</summary>
    public DbSet<CompanyMembership> CompanyMemberships { get; set; } = null!;
    public DbSet<DocumentNumberSequence> DocumentNumberSequences { get; set; } = null!;
    public DbSet<DocumentIdentity> DocumentIdentities { get; set; } = null!;
    public DbSet<DocumentLineIdentity> DocumentLineIdentities { get; set; } = null!;
    public DbSet<DocumentLineLink> DocumentLineLinks { get; set; } = null!;

    /// <summary>Company-owned fiscal-year calendars gating financial posting dates.</summary>
    public DbSet<FiscalYear> FiscalYears { get; set; } = null!;
    public DbSet<FiscalPeriod> FiscalPeriods { get; set; } = null!;
    public DbSet<FiscalPeriodAccountLock> FiscalPeriodAccountLocks { get; set; } = null!;
    public DbSet<BankStatement> BankStatements { get; set; } = null!;
    public DbSet<BankStatementLine> BankStatementLines { get; set; } = null!;
    public DbSet<BankStatementMatch> BankStatementMatches { get; set; } = null!;
    public DbSet<FixedAsset> FixedAssets { get; set; } = null!;
    public DbSet<CardSettlementBatch> CardSettlementBatches { get; set; } = null!;
    public DbSet<CardSettlementMatch> CardSettlementMatches { get; set; } = null!;
    public DbSet<ExpenseClaim> ExpenseClaims { get; set; } = null!;
    public DbSet<ExpenseClaimLine> ExpenseClaimLines { get; set; } = null!;
    public DbSet<CrmLead> CrmLeads { get; set; } = null!;
    public DbSet<CrmOpportunity> CrmOpportunities { get; set; } = null!;
    public DbSet<CrmActivity> CrmActivities { get; set; } = null!;

    /// <summary>Posted, immutable general-ledger journals and their lines.</summary>
    public DbSet<JournalEntry> JournalEntries { get; set; } = null!;
    public DbSet<JournalLine> JournalLines { get; set; } = null!;

    /// <summary>Company posting-policy role-to-account mappings.</summary>
    public DbSet<CompanyAccountMapping> CompanyAccountMappings { get; set; } = null!;

    /// <summary>Posted, immutable goods receipts against approved purchase orders.</summary>
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; } = null!;
    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; } = null!;

    /// <summary>Posted, immutable supplier invoices matched to receipts.</summary>
    public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; } = null!;
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; } = null!;

    /// <summary>Posted, immutable payments and their allocations.</summary>
    public DbSet<PaymentEntry> PaymentEntries { get; set; } = null!;
    public DbSet<PaymentAllocation> PaymentAllocations { get; set; } = null!;

    /// <summary>Approved sales orders (commitments only), posted deliveries and sales invoices.</summary>
    public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
    public DbSet<SalesOrderLine> SalesOrderLines { get; set; } = null!;
    public DbSet<DeliveryNote> DeliveryNotes { get; set; } = null!;
    public DbSet<DeliveryNoteLine> DeliveryNoteLines { get; set; } = null!;
    public DbSet<SalesInvoice> SalesInvoices { get; set; } = null!;
    public DbSet<SalesInvoiceLine> SalesInvoiceLines { get; set; } = null!;

    /// <summary>Company quotations and their snapshotted lines.</summary>
    public DbSet<Quotation> Quotations { get; set; } = null!;
    public DbSet<QuotationLine> QuotationLines { get; set; } = null!;

    /// <summary>Point-of-sale registers, shifts, checkouts and refunds.</summary>
    public DbSet<PosRegister> PosRegisters { get; set; } = null!;
    public DbSet<PosShift> PosShifts { get; set; } = null!;
    public DbSet<PosSale> PosSales { get; set; } = null!;
    public DbSet<PosSaleLine> PosSaleLines { get; set; } = null!;
    public DbSet<PosSaleTender> PosSaleTenders { get; set; } = null!;
    public DbSet<PosRefund> PosRefunds { get; set; } = null!;
    public DbSet<PosRefundLine> PosRefundLines { get; set; } = null!;

    /// <summary>Posted, immutable supplier and sales credit notes.</summary>
    public DbSet<PurchaseCreditNote> PurchaseCreditNotes { get; set; } = null!;
    public DbSet<PurchaseCreditNoteLine> PurchaseCreditNoteLines { get; set; } = null!;
    public DbSet<SalesCreditNote> SalesCreditNotes { get; set; } = null!;
    public DbSet<SalesCreditNoteLine> SalesCreditNoteLines { get; set; } = null!;

    /// <summary>Projects, tasks and approved billable time.</summary>
    public DbSet<Project> Projects { get; set; } = null!;
    public DbSet<ProjectTask> ProjectTasks { get; set; } = null!;
    public DbSet<TimesheetEntry> TimesheetEntries { get; set; } = null!;

    /// <summary>HR: employees, attendance and leave.</summary>
    public DbSet<Employee> Employees { get; set; } = null!;
    public DbSet<AttendanceRecord> AttendanceRecords { get; set; } = null!;
    public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;

    /// <summary>Payroll: salary structures, pay runs and payslips.</summary>
    public DbSet<SalaryStructure> SalaryStructures { get; set; } = null!;
    public DbSet<PayRun> PayRuns { get; set; } = null!;
    public DbSet<Payslip> Payslips { get; set; } = null!;

    /// <summary>Manufacturing: BOMs, work orders and component consumption.</summary>
    public DbSet<BillOfMaterials> BillOfMaterials { get; set; } = null!;
    public DbSet<BillOfMaterialsLine> BillOfMaterialsLines { get; set; } = null!;
    public DbSet<WorkOrder> WorkOrders { get; set; } = null!;
    public DbSet<WorkOrderConsumption> WorkOrderConsumptions { get; set; } = null!;

    /// <summary>Service tickets and annual budgets.</summary>
    public DbSet<ServiceTicket> ServiceTickets { get; set; } = null!;
    public DbSet<Budget> Budgets { get; set; } = null!;

    /// <summary>Multi-currency exchange rates and FX revaluation runs.</summary>
    public DbSet<ExchangeRate> ExchangeRates { get; set; } = null!;
    public DbSet<FxRevaluationRun> FxRevaluationRuns { get; set; } = null!;

    /// <summary>Intercompany relationships and reciprocal transactions.</summary>
    public DbSet<IntercompanyRelationship> IntercompanyRelationships { get; set; } = null!;
    public DbSet<IntercompanyTransaction> IntercompanyTransactions { get; set; } = null!;

    /// <summary>Customer and supplier business collaboration portals.</summary>
    public DbSet<PortalContact> PortalContacts { get; set; } = null!;
    public DbSet<SupplierCollaborationEntry> SupplierCollaborationEntries { get; set; } = null!;

    /// <summary>Price lists, item prices and promotional pricing rules.</summary>
    public DbSet<PriceList> PriceLists { get; set; } = null!;
    public DbSet<ItemPrice> ItemPrices { get; set; } = null!;
    public DbSet<PricingRule> PricingRules { get; set; } = null!;

    /// <summary>Landed cost vouchers and item charge allocations.</summary>
    public DbSet<LandedCostVoucher> LandedCostVouchers { get; set; } = null!;
    public DbSet<LandedCostReceipt> LandedCostReceipts { get; set; } = null!;
    public DbSet<LandedCostCharge> LandedCostCharges { get; set; } = null!;
    public DbSet<LandedCostItemAllocation> LandedCostItemAllocations { get; set; } = null!;

    /// <summary>Material requisitions and supplier quotations.</summary>
    public DbSet<MaterialRequest> MaterialRequests { get; set; } = null!;
    public DbSet<MaterialRequestLine> MaterialRequestLines { get; set; } = null!;
    public DbSet<SupplierQuotation> SupplierQuotations { get; set; } = null!;
    public DbSet<SupplierQuotationLine> SupplierQuotationLines { get; set; } = null!;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogsAreAppendOnly();
        EnsureValuationEntriesAreAppendOnly();
        EnsureDocumentLineLinksAreAppendOnly();
        EnsureStockTransactionsAreAppendOnly();
        EnsureStockCountRecordsAreAppendOnly();
        EnsureStockCountVariancesAreAppendOnly();
        EnsureTransferTransitEntriesAreAppendOnly();
        EnsureTransferTransitSettlementsAreAppendOnly();
        EnsureJournalRecordsAreAppendOnly();
        EnsureGoodsReceiptsAreAppendOnly();
        EnsureProcurementFinanceRecordsAreAppendOnly();
        NormalizeStockLotExpiryDates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditLogsAreAppendOnly();
        EnsureValuationEntriesAreAppendOnly();
        EnsureDocumentLineLinksAreAppendOnly();
        EnsureStockTransactionsAreAppendOnly();
        EnsureStockCountRecordsAreAppendOnly();
        EnsureStockCountVariancesAreAppendOnly();
        EnsureTransferTransitEntriesAreAppendOnly();
        EnsureTransferTransitSettlementsAreAppendOnly();
        EnsureJournalRecordsAreAppendOnly();
        EnsureGoodsReceiptsAreAppendOnly();
        EnsureProcurementFinanceRecordsAreAppendOnly();
        NormalizeStockLotExpiryDates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Saves pending changes, stamping <see cref="AuditableEntity"/> timestamps, translating
    /// soft-delete <see cref="EntityState.Deleted"/> entries to a flag flip, and emitting
    /// <see cref="AuditLog"/> rows.
    /// </summary>
    /// <remarks>
    /// Audit log uses a two-phase write: phase one captures original / current values from the
    /// change tracker (running before <c>base.SaveChangesAsync</c>), phase two resolves any
    /// temporary primary keys that the database assigned during insert and writes the
    /// final <see cref="AuditLog"/> rows. Splitting the work lets us record auto-generated
    /// identities in the audit trail without a round-trip to the database to look them up.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var currentUser = _httpContextAccessor?.HttpContext?.User?.Identity?.Name ?? "System";
        return SaveChangesAsAsync(currentUser, cancellationToken);
    }

    /// <summary>Saves changes with a server-verified actor label for audit entries.</summary>
    public async Task<int> SaveChangesAsAsync(
        string auditUsername,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(auditUsername))
        {
            throw new ArgumentException("An authenticated audit identity is required.", nameof(auditUsername));
        }

        EnsureAuditLogsAreAppendOnly();
        EnsureValuationEntriesAreAppendOnly();
        EnsureDocumentLineLinksAreAppendOnly();
        EnsureStockTransactionsAreAppendOnly();
        EnsureStockCountRecordsAreAppendOnly();
        EnsureStockCountVariancesAreAppendOnly();
        EnsureTransferTransitEntriesAreAppendOnly();
        EnsureTransferTransitSettlementsAreAppendOnly();
        EnsureJournalRecordsAreAppendOnly();
        EnsureGoodsReceiptsAreAppendOnly();
        EnsureProcurementFinanceRecordsAreAppendOnly();
        NormalizeStockLotExpiryDates();
        var currentUser = auditUsername.Trim();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>()
                     .Where(entry => entry.State != EntityState.Unchanged && entry.State != EntityState.Detached))
        {
            // Tenant ownership is taken from the authenticated request, never from client input.
            entry.Entity.TenantId = CurrentTenantId;
        }

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.CreatedBy = currentUser;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = utcNow;
                    entry.Entity.UpdatedBy = currentUser;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Merconiq.Core.Interfaces.ISoftDelete>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
            }
        }

        var auditEntries = OnBeforeSaveChanges(currentUser);
        var result = await base.SaveChangesAsync(cancellationToken);

        if (auditEntries.Any())
        {
            await OnAfterSaveChanges(auditEntries, cancellationToken);
        }

        return result;
    }

    private void EnsureAuditLogsAreAppendOnly()
    {
        if (ChangeTracker.Entries<AuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit logs are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureValuationEntriesAreAppendOnly()
    {
        if (ChangeTracker.Entries<StockValuationEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Stock valuation entries are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureDocumentLineLinksAreAppendOnly()
    {
        if (ChangeTracker.Entries<DocumentLineLink>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Document line links are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureStockTransactionsAreAppendOnly()
    {
        if (ChangeTracker.Entries<StockTransaction>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Stock transactions are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureStockCountRecordsAreAppendOnly()
    {
        if (ChangeTracker.Entries<StockCount>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<StockCountLine>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<StockCountObservation>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Stock-count snapshots and observations are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureStockCountVariancesAreAppendOnly()
    {
        if (ChangeTracker.Entries<StockCountVariance>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Stock-count variances are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureTransferTransitEntriesAreAppendOnly()
    {
        if (ChangeTracker.Entries<TransferTransitEntry>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Transfer transit entries are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureTransferTransitSettlementsAreAppendOnly()
    {
        if (ChangeTracker.Entries<TransferTransitSettlement>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Transfer transit settlements are append-only and cannot be updated or deleted.");
        }
    }

    private void EnsureJournalRecordsAreAppendOnly()
    {
        if (ChangeTracker.Entries<JournalEntry>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<JournalLine>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Journal entries and lines are append-only; corrections must use source-linked reversals.");
        }
    }

    private void EnsureProcurementFinanceRecordsAreAppendOnly()
    {
        if (ChangeTracker.Entries<PurchaseInvoice>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PurchaseInvoiceLine>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PaymentEntry>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PaymentAllocation>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Purchase invoices and payments are append-only; corrections use credit notes and reversals.");
        }
    }

    private void EnsureGoodsReceiptsAreAppendOnly()
    {
        if (ChangeTracker.Entries<GoodsReceipt>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<GoodsReceiptLine>()
                .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "Goods receipts are append-only; corrections must use procurement returns or credit notes.");
        }
    }

    private void NormalizeStockLotExpiryDates()
    {
        foreach (var entry in ChangeTracker.Entries<StockInHand>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.ExpiryDate = StockLotExpiryDate.Normalize(entry.Entity.ExpiryDate);

        foreach (var entry in ChangeTracker.Entries<StockReservation>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.ExpiryDate = StockLotExpiryDate.Normalize(entry.Entity.ExpiryDate);

        foreach (var entry in ChangeTracker.Entries<StockReservationAllocation>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.ExpiryDate = StockLotExpiryDate.Normalize(entry.Entity.ExpiryDate);

        foreach (var entry in ChangeTracker.Entries<StockTransaction>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            entry.Entity.ExpiryDate = StockLotExpiryDate.Normalize(entry.Entity.ExpiryDate);
    }

    private List<AuditEntry> OnBeforeSaveChanges(string username)
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            var auditEntry = new AuditEntry(entry)
            {
                TableName = entry.Entity.GetType().Name,
                UserId = username,
                Action = entry.State switch
                {
                    EntityState.Added => "Insert",
                    EntityState.Deleted => "Delete",
                    _ => null!
                }
            };

            foreach (var property in entry.Properties)
            {
                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    if (property.IsTemporary)
                    {
                        auditEntry.TemporaryProperties.Add(property);
                    }
                    else
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue ?? "";
                    }
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.Action = "Insert";
                        auditEntry.NewValues[propertyName] = property.CurrentValue ?? "";
                        break;

                    case EntityState.Deleted:
                        auditEntry.Action = "Delete";
                        auditEntry.OldValues[propertyName] = property.OriginalValue ?? "";
                        break;

                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.Action = "Update";
                            auditEntry.ChangedColumns.Add(propertyName);
                            auditEntry.OldValues[propertyName] = property.OriginalValue ?? "";
                            auditEntry.NewValues[propertyName] = property.CurrentValue ?? "";
                        }
                        break;
                }
            }

            if (!string.IsNullOrEmpty(auditEntry.Action))
            {
                auditEntries.Add(auditEntry);
            }
        }

        return auditEntries;
    }

    private async Task OnAfterSaveChanges(List<AuditEntry> auditEntries, CancellationToken cancellationToken)
    {
        foreach (var auditEntry in auditEntries)
        {
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                auditEntry.KeyValues[prop.Metadata.Name] = prop.CurrentValue ?? "";
            }
            AuditLogs.Add(auditEntry.ToAuditLog());
        }
        await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ItemCode }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Barcode }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.Property(e => e.ItemCode).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Barcode).HasMaxLength(100);
            entity.Property(e => e.Rate).HasColumnType("decimal(18,2)");
            entity.Property(e => e.PurchaseToBaseFactor).HasColumnType("decimal(18,6)");
            entity.Property(e => e.SalesToBaseFactor).HasColumnType("decimal(18,6)");
            entity.HasOne(e => e.BaseUnit).WithMany()
                .HasForeignKey(e => new { e.BaseUnitId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PurchaseUnit).WithMany()
                .HasForeignKey(e => new { e.PurchaseUnitId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SalesUnit).WithMany()
                .HasForeignKey(e => new { e.SalesUnitId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.SupplierId);
            entity.Property(e => e.ReorderLevel).HasDefaultValue(10);

            entity.HasOne(i => i.Supplier)
                  .WithMany(s => s.Items)
                  .HasForeignKey(i => i.SupplierId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UnitOfMeasure>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.Id, e.TenantId }).IsUnique();
            entity.Property(e => e.DecimalPlaces).HasDefaultValue(0);
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ContactPerson).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.Address).HasMaxLength(500);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.HasIndex(e => new { e.TenantId, e.BranchId });
            entity.HasOne(e => e.Branch)
                  .WithMany(b => b.Locations)
                  .HasForeignKey(e => new { e.BranchId, e.TenantId })
                  .HasPrincipalKey(b => new { b.Id, b.TenantId })
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.LegalName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TradingName).HasMaxLength(200);
            entity.Property(e => e.RegistrationNumber).HasMaxLength(100);
            entity.Property(e => e.TaxIdentifier).HasMaxLength(100);
            entity.Property(e => e.BaseCurrency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.CurrencyScale);
            entity.ToTable("Companies", table => table.HasCheckConstraint(
                "CK_Companies_CurrencyScale",
                "\"CurrencyScale\" IS NULL OR \"CurrencyScale\" BETWEEN 0 AND 4"));
            entity.Property(e => e.CountryCode).HasMaxLength(2);
            entity.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.Id, e.TenantId }).IsUnique();
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CustomerCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ContactEmail).HasMaxLength(254);
            entity.Property(e => e.ContactPhone).HasMaxLength(40);
            entity.Property(e => e.BillingAddress).HasMaxLength(500);
            entity.Property(e => e.ShippingAddress).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreditLimit).HasColumnType("decimal(18,6)");
            entity.Property(e => e.CreditLimitChangeReason).HasMaxLength(500);
            entity.Property(e => e.CreditLimitChangedBy).HasMaxLength(256);
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId, e.TenantId });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_Customers_CreditLimit",
                "\"CreditLimit\" IS NULL OR \"CreditLimit\" >= 0"));
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.CustomerCode })
                .IsUnique()
                .HasDatabaseName("UX_Customers_TenantId_CompanyId_CustomerCode");
            entity.ToTable("Customers", table =>
            {
                table.HasCheckConstraint(
                    "CK_Customers_NormalizedCustomerCode",
                    "\"CustomerCode\" <> '' AND \"CustomerCode\" = btrim(\"CustomerCode\") AND \"CustomerCode\" = upper(\"CustomerCode\")");
                table.HasCheckConstraint(
                    "CK_Customers_PaymentTermDays",
                    "\"PaymentTermDays\" IS NULL OR \"PaymentTermDays\" >= 0");
            });
            entity.HasOne(e => e.Company)
                .WithMany(company => company.Customers)
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChartOfAccount>(entity =>
        {
            entity.HasQueryFilter(account => account.TenantId == CurrentTenantId);
            entity.Property(account => account.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(account => account.AccountCode).HasMaxLength(64).IsRequired();
            entity.Property(account => account.Name).HasMaxLength(200).IsRequired();
            entity.Property(account => account.AccountType).HasMaxLength(64).IsRequired();
            entity.Property(account => account.IsActive).HasDefaultValue(true);
            entity.Property(account => account.AccountClass).HasConversion<string>().HasMaxLength(16);
            entity.HasAlternateKey(account => new { account.Id, account.CompanyId, account.TenantId });
            entity.HasIndex(account => new { account.CompanyId, account.TenantId });
            entity.HasIndex(account => new { account.TenantId, account.CompanyId, account.AccountCode })
                .IsUnique()
                .HasDatabaseName("UX_ChartAccounts_Tenant_Company_Code");
            entity.ToTable("ChartOfAccounts", table =>
            {
                table.HasCheckConstraint(
                    "CK_ChartAccounts_AccountCode",
                    "\"AccountCode\" <> '' AND \"AccountCode\" = btrim(\"AccountCode\")");
                table.HasCheckConstraint(
                    "CK_ChartAccounts_Name",
                    "\"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                table.HasCheckConstraint(
                    "CK_ChartAccounts_AccountType",
                    "\"AccountType\" <> '' AND \"AccountType\" = btrim(\"AccountType\")");
                table.HasCheckConstraint(
                    "CK_ChartAccounts_NoSelfParent",
                    "\"ParentAccountId\" IS NULL OR \"ParentAccountId\" <> \"Id\"");
            });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(account => new { account.CompanyId, account.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(account => account.ParentAccount)
                .WithMany(account => account.ChildAccounts)
                .HasForeignKey(account => new { account.ParentAccountId, account.CompanyId, account.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(128);
            entity.Property(e => e.Code).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.Code }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ExternalId }).IsUnique();
            entity.HasIndex(e => new { e.Id, e.TenantId }).IsUnique();
            entity.HasOne(e => e.Company)
                  .WithMany(c => c.Branches)
                  .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                  .HasPrincipalKey(c => new { c.Id, c.TenantId })
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyMembership>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Capabilities).HasConversion<int>().IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.UserId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.IsActive });
            entity.HasOne(e => e.Company)
                .WithMany(c => c.Memberships)
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(c => new { c.Id, c.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany(u => u.CompanyMemberships)
                .HasForeignKey(e => new { e.UserId, e.TenantId })
                .HasPrincipalKey(u => new { u.Id, u.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockInHand>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.ReservedQuantity).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.QuarantinedQuantity).HasDefaultValue(0).IsRequired();
            entity.ToTable("StockInHand", table => table.HasCheckConstraint(
                "CK_StockInHand_AvailableQuantity",
                "\"Quantity\" >= 0 AND \"ReservedQuantity\" >= 0 AND \"QuarantinedQuantity\" >= 0 AND \"ReservedQuantity\" + \"QuarantinedQuantity\" <= \"Quantity\""));
            entity.HasIndex(e => new { e.TenantId, e.ItemId, e.LocationId, e.BatchNumber, e.ExpiryDate }).IsUnique();

            entity.HasOne(s => s.Item)
                  .WithMany(i => i.StockInHands)
                  .HasForeignKey(s => s.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.Location)
                  .WithMany(l => l.StockInHands)
                  .HasForeignKey(s => new { s.LocationId, s.TenantId })
                  .HasPrincipalKey(l => new { l.Id, l.TenantId })
                  .OnDelete(DeleteBehavior.Cascade);

            // PostgreSQL exposes the inserting transaction's xmin (xid) as a hidden
            // system column on every row. Mapping it to the CLR Version property and
            // marking it as a concurrency token gives us optimistic concurrency with
            // zero schema, trigger, or stored-procedure cost — EF Core compares xmin
            // on UPDATE and raises a concurrency exception if the row was modified.
            entity.Property(s => s.Version)
                  .HasColumnName("xmin")
                  .HasColumnType("xid")
                  .ValueGeneratedOnAddOrUpdate()
                  .IsConcurrencyToken();
        });

        modelBuilder.Entity<StockCount>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.Property(e => e.SnapshotAtUtc).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.SnapshotAtUtc });
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => new { e.LocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company)
                .WithMany()
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCountLine>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ItemCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ItemDescriptionSnapshot).HasMaxLength(500).IsRequired();
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.ToTable("StockCountLines", table => table.HasCheckConstraint(
                "CK_StockCountLines_SnapshotQuantity",
                "\"SnapshotQuantity\" >= 0"));
            entity.HasOne(e => e.StockCount)
                .WithMany(e => e.Lines)
                .HasForeignKey(e => new { e.StockCountId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockCountObservation>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.ToTable("StockCountObservations", table => table.HasCheckConstraint(
                "CK_StockCountObservations_CountedQuantity",
                "\"CountedQuantity\" >= 0"));
            entity.HasOne(e => e.StockCountLine)
                .WithOne(e => e.Observation)
                .HasForeignKey<StockCountObservation>(e => new { e.StockCountLineId, e.TenantId })
                .HasPrincipalKey<StockCountLine>(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockCountVariance>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ApprovedUnitCost).HasColumnType("decimal(18,6)");
            entity.Property(e => e.ValueAdjustment).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => new { e.StockCountLineId, e.TenantId }).IsUnique();
            entity.HasIndex(e => new { e.StockTransactionId, e.TenantId })
                .IsUnique()
                .HasFilter("\"StockTransactionId\" IS NOT NULL");
            entity.ToTable("StockCountVariances", table =>
            {
                table.HasCheckConstraint(
                    "CK_StockCountVariances_Quantities",
                    "\"ExpectedCurrentQuantity\" >= 0 AND \"CountedQuantity\" >= 0 AND \"DeltaQuantity\" = \"CountedQuantity\" - \"ExpectedCurrentQuantity\"");
                table.HasCheckConstraint(
                    "CK_StockCountVariances_Reason",
                    "length(trim(\"Reason\")) > 0");
                table.HasCheckConstraint(
                    "CK_StockCountVariances_ApprovedUnitCost",
                    "\"ApprovedUnitCost\" IS NULL OR \"ApprovedUnitCost\" >= 0");
            });
            entity.HasOne(e => e.StockCountLine)
                .WithOne(line => line.Variance)
                .HasForeignKey<StockCountVariance>(e => new { e.StockCountLineId, e.TenantId })
                .HasPrincipalKey<StockCountLine>(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.StockTransaction)
                .WithMany()
                .HasForeignKey(e => new { e.StockTransactionId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceLineReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.ResolutionReason).HasMaxLength(500);
            entity.Property(e => e.ExpiryExceptionReason).HasMaxLength(500);
            entity.HasIndex(e => new { e.TenantId, e.SourceLineReference }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ItemId, e.LocationId, e.BatchNumber, e.ExpiryDate, e.Status });
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => new { e.LocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<StockReservationAllocation>(entity =>
        {
            entity.HasQueryFilter(allocation => allocation.TenantId == CurrentTenantId);
            entity.Property(allocation => allocation.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(allocation => allocation.BatchNumber).HasMaxLength(100);
            entity.Property(allocation => allocation.ExpiryDate).HasColumnType("timestamp with time zone");
            entity.Property(allocation => allocation.ExpiryExceptionReason).HasMaxLength(500);
            entity.HasIndex(allocation => new { allocation.TenantId, allocation.ReservationId, allocation.Ordinal })
                .IsUnique();
            entity.HasIndex(allocation => new { allocation.TenantId, allocation.ReservationId });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_StockReservationAllocations_Quantities",
                "\"Quantity\" > 0 AND \"ConsumedQuantity\" >= 0 AND \"ConsumedQuantity\" <= \"Quantity\""));
            entity.HasOne(allocation => allocation.Reservation)
                .WithMany(reservation => reservation.Allocations)
                .HasForeignKey(allocation => new { allocation.ReservationId, allocation.TenantId })
                .HasPrincipalKey(reservation => new { reservation.Id, reservation.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(allocation => allocation.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<TransferOrder>(entity =>
        {
            entity.ToTable("TransferOrders", table => table.HasCheckConstraint(
                "CK_TransferOrders_DistinctLocations", "\"FromLocationId\" <> \"ToLocationId\""));
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.Property(e => e.DocumentId)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.Status });
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasOne(e => e.DocumentIdentity)
                .WithOne()
                .HasForeignKey<TransferOrder>(e => new { e.DocumentId, e.TenantId })
                .HasPrincipalKey<DocumentIdentity>(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company)
                .WithMany()
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FromLocation)
                .WithMany()
                .HasForeignKey(e => new { e.FromLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ToLocation)
                .WithMany()
                .HasForeignKey(e => new { e.ToLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransferOrderLine>(entity =>
        {
            entity.ToTable("TransferOrderLines", table =>
            {
                table.HasCheckConstraint("CK_TransferOrderLines_PositiveQuantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_TransferOrderLines_DispatchedQuantity", "\"DispatchedQuantity\" >= 0 AND \"DispatchedQuantity\" <= \"Quantity\"");
            });
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.Property(e => e.DocumentLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentLineId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.DispatchedQuantity).IsRequired();
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasOne(e => e.TransferOrder)
                .WithMany(e => e.Lines)
                .HasForeignKey(e => new { e.TransferOrderId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.DocumentLineIdentity)
                .WithOne()
                .HasForeignKey<TransferOrderLine>(e => new { e.DocumentLineId, e.TenantId })
                .HasPrincipalKey<DocumentLineIdentity>(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransferTransitEntry>(entity =>
        {
            entity.ToTable("TransferTransitEntries", table =>
            {
                table.HasCheckConstraint("CK_TransferTransitEntries_PositiveQuantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_TransferTransitEntries_NonNegativeValue", "\"UnitCost\" >= 0 AND \"TotalValue\" >= 0");
            });
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SourceDocumentLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.SourceDocumentLineId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TotalValue).HasColumnType("decimal(18,6)");
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DispatchedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.TransferOrderLineId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.TransferOrderId, e.DispatchedAt });
            entity.HasIndex(e => new { e.TenantId, e.StockTransactionId }).IsUnique();
            entity.HasOne<TransferOrder>()
                .WithMany()
                .HasForeignKey(e => new { e.TransferOrderId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TransferOrderLine>()
                .WithMany()
                .HasForeignKey(e => new { e.TransferOrderLineId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentLineIdentity>()
                .WithMany()
                .HasForeignKey(e => new { e.SourceDocumentLineId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(e => new { e.FromLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(e => new { e.ToLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StockTransaction>()
                .WithMany()
                .HasForeignKey(e => new { e.StockTransactionId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransferTransitSettlement>(entity =>
        {
            entity.ToTable("TransferTransitSettlements", table =>
            {
                table.HasCheckConstraint("CK_TransferTransitSettlements_PositiveQuantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_TransferTransitSettlements_NonNegativeValue", "\"UnitCost\" >= 0 AND \"TotalValue\" >= 0");
                table.HasCheckConstraint(
                    "CK_TransferTransitSettlements_StockTransactionDisposition",
                    "(\"SettlementType\" = 'WrittenOff' AND \"StockTransactionId\" IS NULL) OR (\"SettlementType\" <> 'WrittenOff' AND \"StockTransactionId\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_TransferTransitSettlements_DocumentIdentityPair",
                    "(\"DocumentId\" IS NULL AND \"DocumentLineId\" IS NULL) OR (\"DocumentId\" IS NOT NULL AND \"DocumentLineId\" IS NOT NULL)");
            });
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DocumentId)
                .HasConversion(
                    id => id.HasValue ? id.Value.Value : (Guid?)null,
                    value => value.HasValue ? new DocumentIdentityId(value.Value) : (DocumentIdentityId?)null)
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.DocumentLineId)
                .HasConversion(
                    id => id.HasValue ? id.Value.Value : (Guid?)null,
                    value => value.HasValue ? new DocumentLineIdentityId(value.Value) : (DocumentLineIdentityId?)null)
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentLineId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.SourceDocumentLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.SourceDocumentLineId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.SettlementType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TotalValue).HasColumnType("decimal(18,6)");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SettledBy).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.HasIndex(e => new { e.TenantId, e.DocumentId })
                .IsUnique()
                .HasFilter("\"DocumentId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.DocumentLineId })
                .IsUnique()
                .HasFilter("\"DocumentLineId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.TransferTransitEntryId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.TransferOrderId, e.SettledAt });
            entity.HasOne(e => e.DocumentIdentity)
                .WithOne()
                .HasForeignKey<TransferTransitSettlement>(e => new { e.DocumentId, e.TenantId })
                .HasPrincipalKey<DocumentIdentity>(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.DocumentLineIdentity)
                .WithOne()
                .HasForeignKey<TransferTransitSettlement>(e => new { e.DocumentId, e.DocumentLineId, e.TenantId })
                .HasPrincipalKey<DocumentLineIdentity>(e => new { e.DocumentId, e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TransferTransitEntry>()
                .WithMany()
                .HasForeignKey(e => new { e.TransferTransitEntryId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TransferOrder>()
                .WithMany()
                .HasForeignKey(e => new { e.TransferOrderId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TransferOrderLine>()
                .WithMany()
                .HasForeignKey(e => new { e.TransferOrderLineId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentLineIdentity>()
                .WithMany()
                .HasForeignKey(e => new { e.SourceDocumentLineId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(e => new { e.CompanyId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>()
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(e => new { e.FromLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(e => new { e.ToLocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StockTransaction>()
                .WithMany()
                .HasForeignKey(e => new { e.StockTransactionId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockValuationBucket>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Value).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => new { e.TenantId, e.ItemId, e.LocationId }).IsUnique();
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => new { e.LocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<StockValuationEntry>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EntryType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TotalValue).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => new { e.TenantId, e.ItemId, e.LocationId });
            entity.HasIndex(e => new { e.TenantId, e.StockTransactionId }).IsUnique();
            entity.HasOne(e => e.StockTransaction)
                .WithMany()
                .HasForeignKey(e => new { e.StockTransactionId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => new { e.ItemId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => new { e.LocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            entity.ToTable("PurchaseOrders", table => table.HasCheckConstraint(
                "CK_PurchaseOrders_ApprovedCommercialVersion",
                "\"Status\" <> 'Approved' OR (\"ApprovedCommercialVersion\" IS NOT NULL AND \"ApprovedCommercialVersion\" = \"CommercialVersion\" AND \"ApprovedCommercialSnapshotJson\" IS NOT NULL)"));
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.TenantId, e.PONumber }).IsUnique();
            entity.Property(e => e.PONumber).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.NetAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.CurrencyScale).IsRequired();
            entity.Property(e => e.CalculationVersion).IsRequired();
            entity.Property(e => e.CommercialVersion).HasDefaultValue(1).IsRequired();
            entity.Property(e => e.ApprovedCommercialSnapshotJson).HasColumnType("jsonb");
            entity.Property(e => e.DeliveryTerms).HasMaxLength(1000);
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.DocumentId)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.PONumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.HasIndex(e => e.SupplierId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.OrderDate);

            entity.HasOne(po => po.DocumentIdentity)
                .WithOne(identity => identity.PurchaseOrder)
                .HasForeignKey<PurchaseOrder>(po => new { po.DocumentId, po.TenantId })
                .HasPrincipalKey<DocumentIdentity>(identity => new { identity.Id, identity.TenantId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(po => po.Supplier)
                  .WithMany(s => s.PurchaseOrders)
                  .HasForeignKey(po => po.SupplierId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.UnitPrice).HasColumnType("decimal(20,4)");
            entity.Property(e => e.DocumentLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(e => e.DocumentLineId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

            entity.HasOne(od => od.PurchaseOrder)
                  .WithMany(po => po.OrderDetails)
                  .HasForeignKey(od => od.PurchaseOrderId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(od => od.Item)
                  .WithMany(i => i.OrderDetails)
                  .HasForeignKey(od => od.ItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.DiscountPercent).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TaxRatePercent).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TaxCategory).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.TaxMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.Direction).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.CurrencyScale).IsRequired();
            entity.Property(e => e.CalculationVersion).IsRequired();
            entity.Property(e => e.NetAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TaxableAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.TaxAmount).HasColumnType("decimal(18,6)");
            entity.Property(e => e.GrossAmount).HasColumnType("decimal(18,6)");

            entity.HasOne(od => od.TaxRule)
                .WithMany()
                .HasForeignKey(od => od.TaxRuleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(od => od.DocumentLineIdentity)
                .WithOne(line => line.OrderDetail)
                .HasForeignKey<OrderDetail>(od => new { od.DocumentLineId, od.TenantId })
                .HasPrincipalKey<DocumentLineIdentity>(line => new { line.Id, line.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaxRule>(entity =>
        {
            entity.HasQueryFilter(rule => rule.TenantId == CurrentTenantId);
            entity.Property(rule => rule.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(rule => rule.Code).HasMaxLength(64).IsRequired();
            entity.Property(rule => rule.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.RatePercent).HasColumnType("decimal(18,6)");
            entity.Property(rule => rule.CalculationMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(rule => rule.EffectiveFromUtc).IsRequired();
            entity.HasIndex(rule => new { rule.TenantId, rule.Code, rule.EffectiveFromUtc }).IsUnique();
            entity.HasIndex(rule => new { rule.TenantId, rule.Code, rule.IsActive });
        });

        modelBuilder.Entity<DocumentIdentity>(entity =>
        {
            entity.HasQueryFilter(document => document.TenantId == CurrentTenantId);
            entity.Property(document => document.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(document => document.Id)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(document => document.DocumentType).HasMaxLength(64).IsRequired();
            entity.Property(document => document.HumanNumber).HasMaxLength(50).IsRequired();
            entity.Property(document => document.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(document => document.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.Property(document => document.RequestScope).HasMaxLength(256).IsRequired();
            entity.Property(document => document.RequestKey).HasMaxLength(200);
            entity.Property(document => document.RequestHash).HasMaxLength(64);
            entity.HasAlternateKey(document => new { document.Id, document.TenantId });
            entity.HasIndex(document => new { document.TenantId, document.DocumentType, document.Period, document.HumanNumber })
                .IsUnique()
                .HasFilter("\"CompanyId\" IS NULL");
            entity.HasIndex(document => new { document.TenantId, document.CompanyId, document.DocumentType, document.Period, document.HumanNumber })
                .IsUnique()
                .HasFilter("\"CompanyId\" IS NOT NULL");
            entity.HasIndex(document => new { document.TenantId, document.RequestScope, document.RequestKey })
                .IsUnique()
                .HasFilter("\"RequestKey\" IS NOT NULL");
            entity.Property(document => document.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.DocumentType).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.HumanNumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.Period).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.RequestScope).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.RequestKey).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(document => document.RequestHash).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.HasOne(document => document.Company)
                .WithMany()
                .HasForeignKey(document => new { document.CompanyId, document.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentLineIdentity>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Id)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(line => line.DocumentId)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value))
                .ValueGeneratedNever();
            entity.Property(line => line.LineType).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(line => new { line.Id, line.TenantId });
            entity.HasOne(line => line.DocumentIdentity)
                .WithMany(document => document.Lines)
                .HasForeignKey(line => new { line.DocumentId, line.TenantId })
                .HasPrincipalKey(document => new { document.Id, document.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(line => line.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(line => line.DocumentId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(line => line.LineType).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        modelBuilder.Entity<DocumentLineLink>(entity =>
        {
            entity.HasQueryFilter(link => link.TenantId == CurrentTenantId);
            entity.Property(link => link.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(link => link.SourceDocumentId)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value));
            entity.Property(link => link.SourceLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value));
            entity.Property(link => link.TargetDocumentId)
                .HasConversion(id => id.Value, value => new DocumentIdentityId(value));
            entity.Property(link => link.TargetLineId)
                .HasConversion(id => id.Value, value => new DocumentLineIdentityId(value));
            entity.Property(link => link.RelationshipType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(link => new
            {
                link.TenantId,
                link.CompanyId,
                link.SourceDocumentId,
                link.SourceLineId,
                link.TargetDocumentId,
                link.TargetLineId,
                link.RelationshipType
            }).IsUnique();
            entity.HasOne(link => link.SourceLine)
                .WithMany()
                .HasForeignKey(link => new { link.SourceDocumentId, link.SourceLineId, link.TenantId })
                .HasPrincipalKey(line => new { line.DocumentId, line.Id, line.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(link => link.TargetLine)
                .WithMany()
                .HasForeignKey(link => new { link.TargetDocumentId, link.TargetLineId, link.TenantId })
                .HasPrincipalKey(line => new { line.DocumentId, line.Id, line.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(link => new { link.CompanyId, link.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockTransaction>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(e => new { e.Id, e.TenantId });
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.TransactionType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.ExpiryExceptionReason).HasMaxLength(500);
            entity.Property(e => e.QuarantineReason).HasMaxLength(500);
            entity.Property(e => e.BatchNumber).HasMaxLength(100);
            entity.Property(e => e.SourceLineReference).HasMaxLength(128);
            entity.Property(e => e.ReturnDisposition).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => e.TransactionDate);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => new { e.ItemId, e.TransactionDate });
            entity.HasIndex(e => new { e.TenantId, e.SourceLineReference })
                  .IsUnique()
                  .HasFilter("\"SourceLineReference\" IS NOT NULL");

            entity.HasOne(st => st.OriginalTransaction)
                  .WithMany()
                  .HasForeignKey(st => new { st.OriginalTransactionId, st.TenantId })
                  .HasPrincipalKey(st => new { st.Id, st.TenantId })
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(st => st.Item)
                  .WithMany(i => i.StockTransactions)
                  .HasForeignKey(st => st.ItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(st => st.FromLocation)
                  .WithMany()
                  .HasForeignKey(st => new { st.FromLocationId, st.TenantId })
                  .HasPrincipalKey(l => new { l.Id, l.TenantId })
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(st => st.ToLocation)
                  .WithMany()
                  .HasForeignKey(st => new { st.ToLocationId, st.TenantId })
                  .HasPrincipalKey(l => new { l.Id, l.TenantId })
                  // Keep tenant identity non-null and preserve transfer history if a location
                  // is hard-deleted. Normal location deletion is soft-delete only.
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<WebhookSubscription>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.LeaseToken).IsConcurrencyToken();
            entity.Property(e => e.LastResponse).HasMaxLength(4096);
            entity.Property(e => e.LastError).HasMaxLength(4096);
            entity.HasIndex(e => e.TenantId);
            entity.HasIndex(e => new { e.EventId, e.SubscriptionId }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt });
            entity.HasIndex(e => e.LeaseToken).IsUnique();
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Scope).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.ResponseBody).HasMaxLength(16384);
            entity.Property(e => e.LastError).HasMaxLength(4096);
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasIndex(e => new { e.TenantId, e.Scope, e.Key }).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<OpeningStockImport>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ImportReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ApprovalReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ApprovedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.ImportReference }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ApprovalReference }).IsUnique();
            entity.HasIndex(e => e.TenantId).IsUnique();
            entity.Property(e => e.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ImportReference).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ApprovalReference).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.RequestHash).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ApprovedBy).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ApprovedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.CutoverAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.LineCount).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        modelBuilder.Entity<OpeningStockImportLine>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExternalReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.UnitCost).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => new { e.OpeningStockImportId, e.ExternalReference }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ItemId, e.LocationId });
            entity.HasOne(e => e.OpeningStockImport)
                .WithMany(e => e.Lines)
                .HasForeignKey(e => new { e.OpeningStockImportId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.StockTransaction)
                .WithMany()
                .HasForeignKey(e => new { e.StockTransactionId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Location)
                .WithMany()
                .HasForeignKey(e => new { e.LocationId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.OpeningStockImportId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.StockTransactionId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.RowNumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ExternalReference).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ItemId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.LocationId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.Quantity).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.UnitCost).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        modelBuilder.Entity<OpeningStockCorrection>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CorrectionReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ApprovalReference).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CorrectedBy).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CorrectionReference }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ApprovalReference }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.OpeningStockImportId }).IsUnique();
            entity.HasOne(e => e.OpeningStockImport)
                .WithMany()
                .HasForeignKey(e => new { e.OpeningStockImportId, e.TenantId })
                .HasPrincipalKey(e => new { e.Id, e.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Id).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.TenantId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.OpeningStockImportId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.CorrectionReference).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.ApprovalReference).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.RequestHash).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.Reason).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.CorrectedBy).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.CorrectedAt).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            entity.Property(e => e.LineCount).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        });

        modelBuilder.Entity<DocumentNumberSequence>(entity =>
        {
            entity.HasQueryFilter(e => e.TenantId == CurrentTenantId);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DocumentType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Prefix).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.DocumentType, e.Period }).IsUnique();
        });

        modelBuilder.Entity<FiscalYear>(entity =>
        {
            entity.HasQueryFilter(year => year.TenantId == CurrentTenantId);
            entity.Property(year => year.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(year => year.Name).HasMaxLength(100).IsRequired();
            entity.HasAlternateKey(year => new { year.Id, year.CompanyId, year.TenantId });
            entity.HasIndex(year => new { year.TenantId, year.CompanyId });
            entity.ToTable("FiscalYears", table => table.HasCheckConstraint(
                "CK_FiscalYears_Range",
                "\"EndDate\" >= \"StartDate\" AND \"Name\" <> '' AND \"Name\" = btrim(\"Name\")"));
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(year => new { year.CompanyId, year.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FiscalPeriod>(entity =>
        {
            entity.HasQueryFilter(period => period.TenantId == CurrentTenantId);
            entity.Property(period => period.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(period => period.Name).HasMaxLength(100).IsRequired();
            entity.Property(period => period.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(period => period.ClosedBy).HasMaxLength(256);
            entity.Property(period => period.ReopenedBy).HasMaxLength(256);
            entity.Property(period => period.ReopenReason).HasMaxLength(500);
            entity.HasAlternateKey(period => new { period.Id, period.CompanyId, period.TenantId });
            entity.HasIndex(period => new { period.TenantId, period.FiscalYearId, period.PeriodNumber }).IsUnique();
            entity.HasIndex(period => new { period.TenantId, period.CompanyId, period.StartDate }).IsUnique();
            entity.ToTable("FiscalPeriods", table =>
            {
                table.HasCheckConstraint(
                    "CK_FiscalPeriods_Range",
                    "\"EndDate\" >= \"StartDate\" AND \"PeriodNumber\" BETWEEN 1 AND 13 AND \"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                table.HasCheckConstraint(
                    "CK_FiscalPeriods_Adjustment",
                    "NOT \"IsAdjustment\" OR (\"PeriodNumber\" = 13 AND \"EndDate\" = \"StartDate\")");
            });
            entity.HasOne(period => period.FiscalYear)
                .WithMany(year => year.Periods)
                .HasForeignKey(period => new { period.FiscalYearId, period.CompanyId, period.TenantId })
                .HasPrincipalKey(year => new { year.Id, year.CompanyId, year.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(period => new { period.CompanyId, period.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FiscalPeriodAccountLock>(entity =>
        {
            entity.HasQueryFilter(entry => entry.TenantId == CurrentTenantId);
            entity.Property(entry => entry.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.LockedBy).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.LockReason).HasMaxLength(500).IsRequired();
            entity.Property(entry => entry.ReleasedBy).HasMaxLength(256);
            entity.Property(entry => entry.ReleaseReason).HasMaxLength(500);
            entity.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.FiscalPeriodId, entry.ChartOfAccountId })
                .IsUnique()
                .HasDatabaseName("UX_FiscalPeriodAccountLocks_Period_Account");
            entity.ToTable("FiscalPeriodAccountLocks", table => table.HasCheckConstraint(
                "CK_FiscalPeriodAccountLocks_Reasons",
                "\"LockReason\" <> '' AND \"LockReason\" = btrim(\"LockReason\") AND " +
                "(\"ReleaseReason\" IS NULL OR (\"ReleaseReason\" <> '' AND \"ReleaseReason\" = btrim(\"ReleaseReason\")))"));
            entity.HasOne(entry => entry.FiscalPeriod)
                .WithMany()
                .HasForeignKey(entry => new { entry.FiscalPeriodId, entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(period => new { period.Id, period.CompanyId, period.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.ChartOfAccount)
                .WithMany()
                .HasForeignKey(entry => new { entry.ChartOfAccountId, entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FixedAsset>(entity =>
        {
            entity.HasQueryFilter(asset => asset.TenantId == CurrentTenantId);
            entity.Property(asset => asset.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(asset => asset.AssetCode).HasMaxLength(64).IsRequired();
            entity.Property(asset => asset.Name).HasMaxLength(200).IsRequired();
            entity.Property(asset => asset.CostAmount).HasColumnType("decimal(18,6)");
            entity.Property(asset => asset.SalvageValue).HasColumnType("decimal(18,6)");
            entity.Property(asset => asset.DisposalProceeds).HasColumnType("decimal(18,6)");
            entity.Property(asset => asset.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(asset => asset.CapitalizedBy).HasMaxLength(256);
            entity.Property(asset => asset.DisposedBy).HasMaxLength(256);
            entity.Property(asset => asset.DisposalReason).HasMaxLength(500);
            entity.HasAlternateKey(asset => new { asset.Id, asset.CompanyId, asset.TenantId });
            entity.HasIndex(asset => new { asset.TenantId, asset.CompanyId, asset.AssetCode })
                .IsUnique()
                .HasDatabaseName("UX_FixedAssets_Company_Code");
            entity.ToTable("FixedAssets", table =>
            {
                table.HasCheckConstraint(
                    "CK_FixedAssets_Code",
                    "\"AssetCode\" <> '' AND \"AssetCode\" = btrim(\"AssetCode\")");
                table.HasCheckConstraint(
                    "CK_FixedAssets_Name",
                    "\"Name\" <> '' AND \"Name\" = btrim(\"Name\")");
                table.HasCheckConstraint(
                    "CK_FixedAssets_Schedule",
                    "\"CostAmount\" > 0 AND \"SalvageValue\" >= 0 AND \"SalvageValue\" < \"CostAmount\" AND " +
                    "\"UsefulLifeMonths\" BETWEEN 1 AND 600 AND \"DepreciationStartDate\" >= \"AcquisitionDate\"");
                table.HasCheckConstraint(
                    "CK_FixedAssets_Lifecycle",
                    "(\"Status\" = 'Registered' AND \"CapitalizationJournalEntryId\" IS NULL AND \"DisposalJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Capitalized' AND \"CapitalizationJournalEntryId\" IS NOT NULL AND \"DisposalJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Disposed' AND \"CapitalizationJournalEntryId\" IS NOT NULL AND \"DisposalJournalEntryId\" IS NOT NULL)");
            });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(asset => new { asset.CompanyId, asset.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(asset => new { asset.CapitalizationJournalEntryId, asset.TenantId })
                .HasPrincipalKey(entry => new { entry.Id, entry.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(asset => new { asset.DisposalJournalEntryId, asset.TenantId })
                .HasPrincipalKey(entry => new { entry.Id, entry.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CardSettlementBatch>(entity =>
        {
            entity.HasQueryFilter(batch => batch.TenantId == CurrentTenantId);
            entity.Property(batch => batch.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(batch => batch.Reference).HasMaxLength(100).IsRequired();
            entity.Property(batch => batch.ImportedBy).HasMaxLength(256).IsRequired();
            entity.Property(batch => batch.GrossAmount).HasColumnType("decimal(18,6)");
            entity.Property(batch => batch.FeeAmount).HasColumnType("decimal(18,6)");
            entity.Property(batch => batch.NetAmount).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(batch => new { batch.Id, batch.CompanyId, batch.TenantId });
            entity.HasIndex(batch => new { batch.TenantId, batch.CompanyId, batch.Reference })
                .IsUnique()
                .HasDatabaseName("UX_CardSettlementBatches_Company_Reference");
            entity.ToTable("CardSettlementBatches", table =>
            {
                table.HasCheckConstraint(
                    "CK_CardSettlementBatches_Reference",
                    "\"Reference\" <> '' AND \"Reference\" = btrim(\"Reference\")");
                table.HasCheckConstraint(
                    "CK_CardSettlementBatches_Amounts",
                    "\"GrossAmount\" > 0 AND \"FeeAmount\" >= 0 AND \"FeeAmount\" <= \"GrossAmount\" AND " +
                    "\"NetAmount\" = \"GrossAmount\" - \"FeeAmount\"");
            });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(batch => new { batch.CompanyId, batch.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CardSettlementMatch>(entity =>
        {
            entity.HasQueryFilter(match => match.TenantId == CurrentTenantId);
            entity.Property(match => match.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(match => match.Amount).HasColumnType("decimal(18,6)");
            entity.Property(match => match.MatchedBy).HasMaxLength(256).IsRequired();
            entity.Property(match => match.UnmatchedBy).HasMaxLength(256);
            entity.Property(match => match.UnmatchReason).HasMaxLength(500);
            // One active settlement per card tender; withdrawn settlements remain as history.
            entity.HasIndex(match => new { match.TenantId, match.CompanyId, match.PosSaleTenderId })
                .IsUnique()
                .HasFilter("\"IsActive\"")
                .HasDatabaseName("UX_CardSettlementMatches_ActiveTender");
            entity.ToTable("CardSettlementMatches", table => table.HasCheckConstraint(
                "CK_CardSettlementMatches_Withdrawal",
                "\"IsActive\" OR (\"UnmatchedAtUtc\" IS NOT NULL AND \"UnmatchReason\" IS NOT NULL AND \"UnmatchReason\" <> '')"));
            entity.HasOne(match => match.CardSettlementBatch)
                .WithMany()
                .HasForeignKey(match => new { match.CardSettlementBatchId, match.CompanyId, match.TenantId })
                .HasPrincipalKey(batch => new { batch.Id, batch.CompanyId, batch.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(match => match.PosSaleTender)
                .WithMany()
                .HasForeignKey(match => new { match.PosSaleTenderId, match.CompanyId, match.TenantId })
                .HasPrincipalKey(tender => new { tender.Id, tender.CompanyId, tender.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CrmLead>(entity =>
        {
            entity.HasQueryFilter(lead => lead.TenantId == CurrentTenantId);
            entity.Property(lead => lead.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(lead => lead.Name).HasMaxLength(200).IsRequired();
            entity.Property(lead => lead.Email).HasMaxLength(254);
            entity.Property(lead => lead.Phone).HasMaxLength(40);
            entity.Property(lead => lead.Source).HasMaxLength(100).IsRequired();
            entity.Property(lead => lead.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(lead => lead.DisqualifiedReason).HasMaxLength(500);
            entity.Property(lead => lead.CreatedByUser).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(lead => new { lead.Id, lead.CompanyId, lead.TenantId });
            entity.ToTable("CrmLeads", table => table.HasCheckConstraint(
                "CK_CrmLeads_Disqualified",
                "(\"Status\" = 'Disqualified') = (\"DisqualifiedReason\" IS NOT NULL)"));
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(lead => new { lead.CompanyId, lead.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CrmOpportunity>(entity =>
        {
            entity.HasQueryFilter(opportunity => opportunity.TenantId == CurrentTenantId);
            entity.Property(opportunity => opportunity.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(opportunity => opportunity.Title).HasMaxLength(200).IsRequired();
            entity.Property(opportunity => opportunity.Owner).HasMaxLength(256).IsRequired();
            entity.Property(opportunity => opportunity.Stage).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(opportunity => opportunity.ExpectedValue).HasColumnType("decimal(18,6)");
            entity.Property(opportunity => opportunity.ClosedBy).HasMaxLength(256);
            entity.Property(opportunity => opportunity.LostReason).HasMaxLength(500);
            entity.Property(opportunity => opportunity.CreatedByUser).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(opportunity => new { opportunity.Id, opportunity.CompanyId, opportunity.TenantId });
            entity.ToTable("CrmOpportunities", table =>
            {
                table.HasCheckConstraint("CK_CrmOpportunities_Value", "\"ExpectedValue\" >= 0");
                table.HasCheckConstraint(
                    "CK_CrmOpportunities_Won",
                    "(\"Stage\" = 'Won') = (\"CustomerId\" IS NOT NULL) AND " +
                    "(\"Stage\" = 'Lost') = (\"LostReason\" IS NOT NULL) AND " +
                    "(\"Stage\" IN ('Won', 'Lost')) = (\"ClosedAtUtc\" IS NOT NULL)");
            });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(opportunity => new { opportunity.CompanyId, opportunity.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CrmLead>()
                .WithMany()
                .HasForeignKey(opportunity => new { opportunity.LeadId, opportunity.CompanyId, opportunity.TenantId })
                .HasPrincipalKey(lead => new { lead.Id, lead.CompanyId, lead.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(opportunity => new { opportunity.CustomerId, opportunity.CompanyId, opportunity.TenantId })
                .HasPrincipalKey(customer => new { customer.Id, customer.CompanyId, customer.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CrmActivity>(entity =>
        {
            entity.HasQueryFilter(activity => activity.TenantId == CurrentTenantId);
            entity.Property(activity => activity.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(activity => activity.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(activity => activity.Summary).HasMaxLength(500).IsRequired();
            entity.Property(activity => activity.RecordedByUser).HasMaxLength(256).IsRequired();
            entity.ToTable("CrmActivities", table => table.HasCheckConstraint(
                "CK_CrmActivities_OneTarget",
                "(\"LeadId\" IS NULL) <> (\"OpportunityId\" IS NULL)"));
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(activity => new { activity.CompanyId, activity.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CrmLead>()
                .WithMany()
                .HasForeignKey(activity => new { activity.LeadId, activity.CompanyId, activity.TenantId })
                .HasPrincipalKey(lead => new { lead.Id, lead.CompanyId, lead.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CrmOpportunity>()
                .WithMany()
                .HasForeignKey(activity => new { activity.OpportunityId, activity.CompanyId, activity.TenantId })
                .HasPrincipalKey(opportunity => new { opportunity.Id, opportunity.CompanyId, opportunity.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseClaim>(entity =>
        {
            entity.HasQueryFilter(claim => claim.TenantId == CurrentTenantId);
            entity.Property(claim => claim.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(claim => claim.ClaimNumber).HasMaxLength(64).IsRequired();
            entity.Property(claim => claim.ClaimantName).HasMaxLength(200).IsRequired();
            entity.Property(claim => claim.Description).HasMaxLength(500).IsRequired();
            entity.Property(claim => claim.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(claim => claim.TotalAmount).HasColumnType("decimal(18,6)");
            entity.Property(claim => claim.CreatedByUser).HasMaxLength(256).IsRequired();
            entity.Property(claim => claim.SubmittedBy).HasMaxLength(256);
            entity.Property(claim => claim.ApprovedBy).HasMaxLength(256);
            entity.Property(claim => claim.RejectedBy).HasMaxLength(256);
            entity.Property(claim => claim.RejectionReason).HasMaxLength(500);
            entity.Property(claim => claim.PaidBy).HasMaxLength(256);
            entity.HasAlternateKey(claim => new { claim.Id, claim.CompanyId, claim.TenantId });
            entity.HasIndex(claim => new { claim.TenantId, claim.CompanyId, claim.ClaimNumber })
                .IsUnique()
                .HasDatabaseName("UX_ExpenseClaims_Company_Number");
            entity.ToTable("ExpenseClaims", table =>
            {
                table.HasCheckConstraint("CK_ExpenseClaims_Total", "\"TotalAmount\" > 0");
                table.HasCheckConstraint(
                    "CK_ExpenseClaims_Lifecycle",
                    "(\"Status\" = 'Draft' AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Submitted' AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Rejected' AND \"RejectionReason\" IS NOT NULL AND \"ApprovalJournalEntryId\" IS NULL AND \"PaymentJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Approved' AND \"ApprovalJournalEntryId\" IS NOT NULL AND \"PaymentJournalEntryId\" IS NULL) OR " +
                    "(\"Status\" = 'Paid' AND \"ApprovalJournalEntryId\" IS NOT NULL AND \"PaymentJournalEntryId\" IS NOT NULL)");
            });
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(claim => new { claim.CompanyId, claim.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(claim => new { claim.ApprovalJournalEntryId, claim.TenantId })
                .HasPrincipalKey(entry => new { entry.Id, entry.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(claim => new { claim.PaymentJournalEntryId, claim.TenantId })
                .HasPrincipalKey(entry => new { entry.Id, entry.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseClaimLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(500).IsRequired();
            entity.Property(line => line.Amount).HasColumnType("decimal(18,6)");
            entity.HasIndex(line => new { line.TenantId, line.ExpenseClaimId, line.LineNumber }).IsUnique();
            entity.ToTable("ExpenseClaimLines", table => table.HasCheckConstraint("CK_ExpenseClaimLines_Amount", "\"Amount\" > 0"));
            entity.HasOne(line => line.ExpenseClaim)
                .WithMany(claim => claim.Lines)
                .HasForeignKey(line => new { line.ExpenseClaimId, line.CompanyId, line.TenantId })
                .HasPrincipalKey(claim => new { claim.Id, claim.CompanyId, claim.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChartOfAccount>()
                .WithMany()
                .HasForeignKey(line => new { line.ExpenseChartOfAccountId, line.CompanyId, line.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatement>(entity =>
        {
            entity.HasQueryFilter(statement => statement.TenantId == CurrentTenantId);
            entity.Property(statement => statement.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(statement => statement.Reference).HasMaxLength(100);
            entity.Property(statement => statement.ImportedBy).HasMaxLength(256).IsRequired();
            entity.Property(statement => statement.OpeningBalance).HasColumnType("decimal(18,6)");
            entity.Property(statement => statement.ClosingBalance).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(statement => new { statement.Id, statement.CompanyId, statement.TenantId });
            entity.HasIndex(statement => new { statement.TenantId, statement.CompanyId, statement.ChartOfAccountId, statement.StatementDate })
                .IsUnique()
                .HasDatabaseName("UX_BankStatements_Account_Date");
            entity.ToTable("BankStatements", table => table.HasCheckConstraint(
                "CK_BankStatements_Reference",
                "\"Reference\" IS NULL OR (\"Reference\" <> '' AND \"Reference\" = btrim(\"Reference\"))"));
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(statement => new { statement.CompanyId, statement.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChartOfAccount>()
                .WithMany()
                .HasForeignKey(statement => new { statement.ChartOfAccountId, statement.CompanyId, statement.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(500).IsRequired();
            entity.Property(line => line.Reference).HasMaxLength(100);
            entity.Property(line => line.Amount).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(line => new { line.Id, line.CompanyId, line.TenantId });
            entity.HasIndex(line => new { line.TenantId, line.BankStatementId, line.LineNumber }).IsUnique();
            entity.ToTable("BankStatementLines", table =>
            {
                table.HasCheckConstraint("CK_BankStatementLines_Amount", "\"Amount\" <> 0");
                table.HasCheckConstraint(
                    "CK_BankStatementLines_Description",
                    "\"Description\" <> '' AND \"Description\" = btrim(\"Description\")");
            });
            entity.HasOne(line => line.BankStatement)
                .WithMany(statement => statement.Lines)
                .HasForeignKey(line => new { line.BankStatementId, line.CompanyId, line.TenantId })
                .HasPrincipalKey(statement => new { statement.Id, statement.CompanyId, statement.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BankStatementMatch>(entity =>
        {
            entity.HasQueryFilter(match => match.TenantId == CurrentTenantId);
            entity.Property(match => match.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(match => match.MatchedBy).HasMaxLength(256).IsRequired();
            entity.Property(match => match.UnmatchedBy).HasMaxLength(256);
            entity.Property(match => match.UnmatchReason).HasMaxLength(500);
            // At most one active match per statement line and per ledger line; withdrawn matches are history.
            entity.HasIndex(match => new { match.TenantId, match.CompanyId, match.BankStatementLineId })
                .IsUnique()
                .HasFilter("\"IsActive\"")
                .HasDatabaseName("UX_BankStatementMatches_ActiveLine");
            entity.HasIndex(match => new { match.TenantId, match.CompanyId, match.JournalLineId })
                .IsUnique()
                .HasFilter("\"IsActive\"")
                .HasDatabaseName("UX_BankStatementMatches_ActiveJournalLine");
            entity.ToTable("BankStatementMatches", table => table.HasCheckConstraint(
                "CK_BankStatementMatches_Withdrawal",
                "\"IsActive\" OR (\"UnmatchedAtUtc\" IS NOT NULL AND \"UnmatchReason\" IS NOT NULL AND \"UnmatchReason\" <> '')"));
            entity.HasOne(match => match.BankStatementLine)
                .WithMany()
                .HasForeignKey(match => new { match.BankStatementLineId, match.CompanyId, match.TenantId })
                .HasPrincipalKey(line => new { line.Id, line.CompanyId, line.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(match => match.JournalLine)
                .WithMany()
                .HasForeignKey(match => new { match.JournalLineId, match.CompanyId, match.TenantId })
                .HasPrincipalKey(line => new { line.Id, line.CompanyId, line.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.HasQueryFilter(entry => entry.TenantId == CurrentTenantId);
            entity.Property(entry => entry.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.JournalNumber).HasMaxLength(50).IsRequired();
            entity.Property(entry => entry.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(entry => entry.Description).HasMaxLength(500).IsRequired();
            entity.Property(entry => entry.SourceDocumentType).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.SourceDocumentId).HasMaxLength(128).IsRequired();
            entity.Property(entry => entry.SourceAction).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.PostedBy).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.ReversalReason).HasMaxLength(500);
            entity.Property(entry => entry.TotalDebit).HasColumnType("decimal(18,6)");
            entity.Property(entry => entry.TotalCredit).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(entry => new { entry.Id, entry.TenantId });
            entity.HasIndex(entry => new
            {
                entry.TenantId,
                entry.CompanyId,
                entry.SourceDocumentType,
                entry.SourceDocumentId,
                entry.SourceAction,
                entry.SourceVersion
            }).IsUnique().HasDatabaseName("UX_JournalEntries_SourceAction");
            entity.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.JournalNumber }).IsUnique();
            entity.HasIndex(entry => new { entry.TenantId, entry.CompanyId, entry.PostingDate });
            entity.ToTable("JournalEntries", table => table.HasCheckConstraint(
                "CK_JournalEntries_Balanced",
                "\"TotalDebit\" >= 0 AND \"TotalCredit\" >= 0 AND \"TotalDebit\" = \"TotalCredit\" AND \"TotalDebit\" > 0"));
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.ReversalOfJournalEntry)
                .WithMany()
                .HasForeignKey(entry => new { entry.ReversalOfJournalEntryId, entry.TenantId })
                .HasPrincipalKey(original => new { original.Id, original.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FiscalPeriod>()
                .WithMany()
                .HasForeignKey(entry => new { entry.FiscalPeriodId, entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(period => new { period.Id, period.CompanyId, period.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.AccountCodeSnapshot).HasMaxLength(64).IsRequired();
            entity.Property(line => line.AccountNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(500);
            entity.Property(line => line.Debit).HasColumnType("decimal(18,6)");
            entity.Property(line => line.Credit).HasColumnType("decimal(18,6)");
            entity.HasIndex(line => new { line.TenantId, line.JournalEntryId, line.LineNumber }).IsUnique();
            entity.HasAlternateKey(line => new { line.Id, line.CompanyId, line.TenantId });
            entity.HasIndex(line => new { line.TenantId, line.CompanyId, line.ChartOfAccountId });
            entity.ToTable("JournalLines", table => table.HasCheckConstraint(
                "CK_JournalLines_OneSide",
                "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0))"));
            entity.HasOne(line => line.JournalEntry)
                .WithMany(entry => entry.Lines)
                .HasForeignKey(line => new { line.JournalEntryId, line.TenantId })
                .HasPrincipalKey(entry => new { entry.Id, entry.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChartOfAccount>()
                .WithMany()
                .HasForeignKey(line => new { line.ChartOfAccountId, line.CompanyId, line.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyAccountMapping>(entity =>
        {
            entity.HasQueryFilter(mapping => mapping.TenantId == CurrentTenantId);
            entity.Property(mapping => mapping.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(mapping => mapping.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(mapping => new { mapping.TenantId, mapping.CompanyId, mapping.Role }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(mapping => new { mapping.CompanyId, mapping.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(mapping => mapping.Account)
                .WithMany()
                .HasForeignKey(mapping => new { mapping.ChartOfAccountId, mapping.CompanyId, mapping.TenantId })
                .HasPrincipalKey(account => new { account.Id, account.CompanyId, account.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceipt>(entity =>
        {
            entity.HasQueryFilter(receipt => receipt.TenantId == CurrentTenantId);
            entity.Property(receipt => receipt.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(receipt => receipt.ReceiptNumber).HasMaxLength(50).IsRequired();
            entity.Property(receipt => receipt.SupplierDeliveryReference).HasMaxLength(128);
            entity.Property(receipt => receipt.Notes).HasMaxLength(500);
            entity.Property(receipt => receipt.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(receipt => receipt.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(receipt => receipt.ReceivedBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(receipt => new { receipt.Id, receipt.TenantId });
            entity.HasIndex(receipt => new { receipt.TenantId, receipt.CompanyId, receipt.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_GoodsReceipts_RequestKey");
            entity.HasIndex(receipt => new { receipt.TenantId, receipt.CompanyId, receipt.PurchaseOrderId });
            entity.HasOne(receipt => receipt.PurchaseOrder)
                .WithMany()
                .HasForeignKey(receipt => receipt.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(receipt => receipt.Location)
                .WithMany()
                .HasForeignKey(receipt => new { receipt.LocationId, receipt.TenantId })
                .HasPrincipalKey(location => new { location.Id, location.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceiptLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.BatchNumber).HasMaxLength(100);
            entity.ToTable("GoodsReceiptLines", table => table.HasCheckConstraint(
                "CK_GoodsReceiptLines_Quantities",
                "\"AcceptedQuantity\" >= 0 AND \"RejectedQuantity\" >= 0 AND \"AcceptedQuantity\" + \"RejectedQuantity\" > 0 AND \"BaseQuantity\" >= 0 AND \"UnitCost\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.GoodsReceiptId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.PurchaseOrderLineId });
            entity.HasOne(line => line.GoodsReceipt)
                .WithMany(receipt => receipt.Lines)
                .HasForeignKey(line => new { line.GoodsReceiptId, line.TenantId })
                .HasPrincipalKey(receipt => new { receipt.Id, receipt.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.PurchaseOrderLine)
                .WithMany()
                .HasForeignKey(line => line.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseInvoice>(entity =>
        {
            entity.HasQueryFilter(invoice => invoice.TenantId == CurrentTenantId);
            entity.Property(invoice => invoice.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(128).IsRequired();
            entity.Property(invoice => invoice.Notes).HasMaxLength(500);
            entity.Property(invoice => invoice.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(invoice => invoice.PostedBy).HasMaxLength(256).IsRequired();
            entity.Property(invoice => invoice.TotalValue).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(invoice => new { invoice.Id, invoice.TenantId });
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PurchaseInvoices_RequestKey");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.SupplierId, invoice.InvoiceNumber })
                .IsUnique()
                .HasDatabaseName("UX_PurchaseInvoices_SupplierNumber");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.DueDate });
            entity.HasOne(invoice => invoice.Supplier)
                .WithMany()
                .HasForeignKey(invoice => invoice.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseInvoiceLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.MatchedValue).HasColumnType("decimal(18,6)");
            entity.HasIndex(line => new { line.TenantId, line.PurchaseInvoiceId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.GoodsReceiptLineId })
                .IsUnique()
                .HasDatabaseName("UX_PurchaseInvoiceLines_ReceiptLine");
            entity.HasOne(line => line.PurchaseInvoice)
                .WithMany(invoice => invoice.Lines)
                .HasForeignKey(line => new { line.PurchaseInvoiceId, line.TenantId })
                .HasPrincipalKey(invoice => new { invoice.Id, invoice.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.GoodsReceiptLine)
                .WithMany()
                .HasForeignKey(line => line.GoodsReceiptLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentEntry>(entity =>
        {
            entity.HasQueryFilter(payment => payment.TenantId == CurrentTenantId);
            entity.Property(payment => payment.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.PaymentType).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(payment => payment.Amount).HasColumnType("decimal(18,6)");
            entity.Property(payment => payment.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.Reference).HasMaxLength(128);
            entity.Property(payment => payment.Notes).HasMaxLength(500);
            entity.Property(payment => payment.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(payment => payment.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.PostedBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(payment => new { payment.Id, payment.TenantId });
            entity.HasIndex(payment => new { payment.TenantId, payment.CompanyId, payment.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PaymentEntries_RequestKey");
            entity.HasIndex(payment => new { payment.TenantId, payment.CompanyId, payment.PostingDate });
            entity.Property(payment => payment.FromAccountId).HasColumnType("integer");
            entity.Property(payment => payment.ToAccountId).HasColumnType("integer");
            entity.HasOne(payment => payment.Supplier)
                .WithMany()
                .HasForeignKey(payment => payment.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Customer)
                .WithMany()
                .HasForeignKey(payment => payment.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(payment => new { payment.CompanyId, payment.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentAllocation>(entity =>
        {
            entity.HasQueryFilter(allocation => allocation.TenantId == CurrentTenantId);
            entity.Property(allocation => allocation.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(allocation => allocation.Amount).HasColumnType("decimal(18,6)");
            entity.HasIndex(allocation => new { allocation.TenantId, allocation.PaymentEntryId, allocation.LineNumber })
                .IsUnique();
            entity.HasIndex(allocation => new { allocation.TenantId, allocation.PurchaseInvoiceId })
                .HasFilter("\"PurchaseInvoiceId\" IS NOT NULL");
            entity.HasIndex(allocation => new { allocation.TenantId, allocation.SalesInvoiceId })
                .HasFilter("\"SalesInvoiceId\" IS NOT NULL");
            entity.ToTable("PaymentAllocations", table => table.HasCheckConstraint(
                "CK_PaymentAllocations_OneTarget",
                "(\"PurchaseInvoiceId\" IS NOT NULL)::int + (\"SalesInvoiceId\" IS NOT NULL)::int = 1"));
            entity.HasOne(allocation => allocation.PaymentEntry)
                .WithMany(payment => payment.Allocations)
                .HasForeignKey(allocation => new { allocation.PaymentEntryId, allocation.TenantId })
                .HasPrincipalKey(payment => new { payment.Id, payment.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(allocation => allocation.PurchaseInvoice)
                .WithMany()
                .HasForeignKey(allocation => allocation.PurchaseInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(allocation => allocation.SalesInvoice)
                .WithMany()
                .HasForeignKey(allocation => allocation.SalesInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(allocation => new { allocation.CompanyId, allocation.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrder>(entity =>
        {
            entity.HasQueryFilter(order => order.TenantId == CurrentTenantId);
            entity.Property(order => order.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(order => order.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(order => order.Notes).HasMaxLength(500);
            entity.Property(order => order.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(order => order.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(order => order.ApprovedBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(order => new { order.Id, order.TenantId });
            entity.HasIndex(order => new { order.TenantId, order.CompanyId, order.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_SalesOrders_RequestKey");
            entity.HasIndex(order => new { order.TenantId, order.CompanyId, order.OrderNumber }).IsUnique();
            entity.HasOne(order => order.Customer)
                .WithMany()
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrderLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.UnitPrice).HasColumnType("decimal(18,6)");
            entity.Property(line => line.NetValue).HasColumnType("decimal(18,6)");
            entity.ToTable("SalesOrderLines", table => table.HasCheckConstraint(
                "CK_SalesOrderLines_Quantities",
                "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"NetValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.SalesOrderId, line.LineNumber }).IsUnique();
            entity.HasOne(line => line.SalesOrder)
                .WithMany(order => order.Lines)
                .HasForeignKey(line => new { line.SalesOrderId, line.TenantId })
                .HasPrincipalKey(order => new { order.Id, order.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryNote>(entity =>
        {
            entity.HasQueryFilter(delivery => delivery.TenantId == CurrentTenantId);
            entity.Property(delivery => delivery.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(delivery => delivery.DeliveryNumber).HasMaxLength(50).IsRequired();
            entity.Property(delivery => delivery.Notes).HasMaxLength(500);
            entity.Property(delivery => delivery.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(delivery => delivery.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(delivery => delivery.DeliveredBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(delivery => new { delivery.Id, delivery.TenantId });
            entity.HasIndex(delivery => new { delivery.TenantId, delivery.CompanyId, delivery.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_DeliveryNotes_RequestKey");
            entity.HasIndex(delivery => new { delivery.TenantId, delivery.CompanyId, delivery.SalesOrderId });
            entity.HasOne(delivery => delivery.SalesOrder)
                .WithMany()
                .HasForeignKey(delivery => delivery.SalesOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(delivery => delivery.Location)
                .WithMany()
                .HasForeignKey(delivery => new { delivery.LocationId, delivery.TenantId })
                .HasPrincipalKey(location => new { location.Id, location.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(delivery => new { delivery.CompanyId, delivery.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryNoteLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.BatchNumber).HasMaxLength(100);
            entity.ToTable("DeliveryNoteLines", table => table.HasCheckConstraint(
                "CK_DeliveryNoteLines_Quantity",
                "\"Quantity\" > 0"));
            entity.HasIndex(line => new { line.TenantId, line.DeliveryNoteId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.SalesOrderLineId });
            entity.HasIndex(line => new { line.TenantId, line.StockTransactionId }).IsUnique();
            entity.HasOne(line => line.DeliveryNote)
                .WithMany(delivery => delivery.Lines)
                .HasForeignKey(line => new { line.DeliveryNoteId, line.TenantId })
                .HasPrincipalKey(delivery => new { delivery.Id, delivery.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.SalesOrderLine)
                .WithMany()
                .HasForeignKey(line => line.SalesOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.StockTransaction)
                .WithMany()
                .HasForeignKey(line => new { line.StockTransactionId, line.TenantId })
                .HasPrincipalKey(transaction => new { transaction.Id, transaction.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            entity.Property(invoice => invoice.CreditLimitOverrideReason).HasMaxLength(500);
            entity.Property(invoice => invoice.CreditLimitOverrideBy).HasMaxLength(256);
            entity.HasQueryFilter(invoice => invoice.TenantId == CurrentTenantId);
            entity.Property(invoice => invoice.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(128).IsRequired();
            entity.Property(invoice => invoice.Notes).HasMaxLength(500);
            entity.Property(invoice => invoice.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(invoice => invoice.PostedBy).HasMaxLength(256).IsRequired();
            entity.Property(invoice => invoice.TotalValue).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(invoice => new { invoice.Id, invoice.TenantId });
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_SalesInvoices_RequestKey");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.InvoiceNumber })
                .IsUnique()
                .HasDatabaseName("UX_SalesInvoices_InvoiceNumber");
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.CompanyId, invoice.DueDate });
            entity.HasOne(invoice => invoice.Customer)
                .WithMany()
                .HasForeignKey(invoice => invoice.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesInvoiceLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.RevenueValue).HasColumnType("decimal(18,6)");
            entity.HasIndex(line => new { line.TenantId, line.SalesInvoiceId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.DeliveryNoteLineId })
                .IsUnique()
                .HasFilter("\"DeliveryNoteLineId\" IS NOT NULL")
                .HasDatabaseName("UX_SalesInvoiceLines_DeliveryLine");
            entity.HasIndex(line => new { line.TenantId, line.TimesheetEntryId })
                .IsUnique()
                .HasFilter("\"TimesheetEntryId\" IS NOT NULL")
                .HasDatabaseName("UX_SalesInvoiceLines_TimesheetEntry");
            entity.ToTable("SalesInvoiceLines", table => table.HasCheckConstraint(
                "CK_SalesInvoiceLines_OneTarget",
                "(\"DeliveryNoteLineId\" IS NOT NULL)::int + (\"TimesheetEntryId\" IS NOT NULL)::int = 1"));
            entity.HasOne(line => line.SalesInvoice)
                .WithMany(invoice => invoice.Lines)
                .HasForeignKey(line => new { line.SalesInvoiceId, line.TenantId })
                .HasPrincipalKey(invoice => new { invoice.Id, invoice.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.DeliveryNoteLine)
                .WithMany()
                .HasForeignKey(line => line.DeliveryNoteLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.TimesheetEntry)
                .WithMany()
                .HasForeignKey(line => line.TimesheetEntryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosRegister>(entity =>
        {
            entity.HasQueryFilter(register => register.TenantId == CurrentTenantId);
            entity.Property(register => register.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(register => register.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(register => new { register.TenantId, register.CompanyId, register.Name }).IsUnique();
            entity.Property(register => register.VarianceApprovalThreshold).HasColumnType("decimal(18,6)");
            entity.HasOne(register => register.Location)
                .WithMany()
                .HasForeignKey(register => new { register.LocationId, register.TenantId })
                .HasPrincipalKey(location => new { location.Id, location.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(register => new { register.CompanyId, register.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosShift>(entity =>
        {
            entity.HasQueryFilter(shift => shift.TenantId == CurrentTenantId);
            entity.Property(shift => shift.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(shift => shift.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(shift => shift.OpenedBy).HasMaxLength(256).IsRequired();
            entity.Property(shift => shift.ClosedBy).HasMaxLength(256);
            entity.Property(shift => shift.CloseApprovedBy).HasMaxLength(256);
            entity.Property(shift => shift.CloseRejectedBy).HasMaxLength(256);
            entity.Property(shift => shift.CloseRejectionReason).HasMaxLength(500);
            entity.Property(shift => shift.OpeningFloat).HasColumnType("decimal(18,6)");
            entity.Property(shift => shift.CountedCash).HasColumnType("decimal(18,6)");
            entity.Property(shift => shift.ExpectedCash).HasColumnType("decimal(18,6)");
            entity.Property(shift => shift.OverShortValue).HasColumnType("decimal(18,6)");
            entity.HasIndex(shift => new { shift.TenantId, shift.RegisterId, shift.Status });
            entity.HasOne(shift => shift.Register)
                .WithMany()
                .HasForeignKey(shift => new { shift.RegisterId, shift.TenantId })
                .HasPrincipalKey(register => new { register.Id, register.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(shift => new { shift.CompanyId, shift.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosSale>(entity =>
        {
            entity.HasQueryFilter(sale => sale.TenantId == CurrentTenantId);
            entity.Property(sale => sale.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(sale => sale.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(sale => sale.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(sale => sale.TotalValue).HasColumnType("decimal(18,6)");
            entity.Property(sale => sale.SoldBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(sale => new { sale.Id, sale.TenantId });
            entity.HasIndex(sale => new { sale.TenantId, sale.CompanyId, sale.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PosSales_RequestKey");
            entity.HasIndex(sale => new { sale.TenantId, sale.ShiftId });
            entity.HasOne(sale => sale.Register)
                .WithMany()
                .HasForeignKey(sale => new { sale.RegisterId, sale.TenantId })
                .HasPrincipalKey(register => new { register.Id, register.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(sale => sale.Shift)
                .WithMany()
                .HasForeignKey(sale => new { sale.ShiftId, sale.TenantId })
                .HasPrincipalKey(shift => new { shift.Id, shift.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(sale => sale.Customer)
                .WithMany()
                .HasForeignKey(sale => sale.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(sale => new { sale.CompanyId, sale.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosSaleLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.UnitPrice).HasColumnType("decimal(18,6)");
            entity.Property(line => line.LineValue).HasColumnType("decimal(18,6)");
            entity.ToTable("PosSaleLines", table => table.HasCheckConstraint(
                "CK_PosSaleLines_Values",
                "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"LineValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.PosSaleId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.StockTransactionId }).IsUnique();
            entity.HasOne(line => line.PosSale)
                .WithMany(sale => sale.Lines)
                .HasForeignKey(line => new { line.PosSaleId, line.TenantId })
                .HasPrincipalKey(sale => new { sale.Id, sale.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.StockTransaction)
                .WithMany()
                .HasForeignKey(line => new { line.StockTransactionId, line.TenantId })
                .HasPrincipalKey(transaction => new { transaction.Id, transaction.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosSaleTender>(entity =>
        {
            entity.HasQueryFilter(tender => tender.TenantId == CurrentTenantId);
            entity.Property(tender => tender.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(tender => tender.Method).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(tender => tender.Amount).HasColumnType("decimal(18,6)");
            entity.HasIndex(tender => new { tender.TenantId, tender.PosSaleId, tender.LineNumber }).IsUnique();
            entity.HasAlternateKey(tender => new { tender.Id, tender.CompanyId, tender.TenantId });
            entity.HasOne(tender => tender.PosSale)
                .WithMany(sale => sale.Tenders)
                .HasForeignKey(tender => new { tender.PosSaleId, tender.TenantId })
                .HasPrincipalKey(sale => new { sale.Id, sale.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(tender => new { tender.CompanyId, tender.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosRefund>(entity =>
        {
            entity.HasQueryFilter(refund => refund.TenantId == CurrentTenantId);
            entity.Property(refund => refund.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(refund => refund.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(refund => refund.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(refund => refund.TotalValue).HasColumnType("decimal(18,6)");
            entity.Property(refund => refund.RefundedBy).HasMaxLength(256).IsRequired();
            entity.Property(refund => refund.Reason).HasMaxLength(500).IsRequired();
            entity.HasAlternateKey(refund => new { refund.Id, refund.TenantId });
            entity.HasIndex(refund => new { refund.TenantId, refund.CompanyId, refund.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PosRefunds_RequestKey");
            entity.HasIndex(refund => new { refund.TenantId, refund.OriginalPosSaleId });
            entity.HasOne(refund => refund.OriginalPosSale)
                .WithMany()
                .HasForeignKey(refund => new { refund.OriginalPosSaleId, refund.TenantId })
                .HasPrincipalKey(sale => new { sale.Id, sale.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(refund => new { refund.CompanyId, refund.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PosRefundLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Disposition).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(line => line.RefundValue).HasColumnType("decimal(18,6)");
            entity.ToTable("PosRefundLines", table => table.HasCheckConstraint(
                "CK_PosRefundLines_Quantity",
                "\"Quantity\" > 0 AND \"RefundValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.PosRefundId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.PosSaleLineId });
            entity.HasOne(line => line.PosRefund)
                .WithMany(refund => refund.Lines)
                .HasForeignKey(line => new { line.PosRefundId, line.TenantId })
                .HasPrincipalKey(refund => new { refund.Id, refund.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.PosSaleLine)
                .WithMany()
                .HasForeignKey(line => line.PosSaleLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseCreditNote>(entity =>
        {
            entity.HasQueryFilter(note => note.TenantId == CurrentTenantId);
            entity.Property(note => note.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(note => note.CreditNoteNumber).HasMaxLength(128).IsRequired();
            entity.Property(note => note.Reason).HasMaxLength(500).IsRequired();
            entity.Property(note => note.Notes).HasMaxLength(500);
            entity.Property(note => note.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(note => note.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(note => note.PostedBy).HasMaxLength(256).IsRequired();
            entity.Property(note => note.TotalValue).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(note => new { note.Id, note.TenantId });
            entity.HasIndex(note => new { note.TenantId, note.CompanyId, note.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PurchaseCreditNotes_RequestKey");
            entity.HasIndex(note => new { note.TenantId, note.CompanyId, note.SupplierId, note.CreditNoteNumber })
                .IsUnique()
                .HasDatabaseName("UX_PurchaseCreditNotes_SupplierNumber");
            entity.HasOne(note => note.Supplier)
                .WithMany()
                .HasForeignKey(note => note.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(note => new { note.CompanyId, note.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseCreditNoteLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.CreditValue).HasColumnType("decimal(18,6)");
            entity.ToTable("PurchaseCreditNoteLines", table => table.HasCheckConstraint(
                "CK_PurchaseCreditNoteLines_Quantity",
                "\"Quantity\" > 0 AND \"CreditValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.PurchaseCreditNoteId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.GoodsReceiptLineId });
            entity.HasOne(line => line.PurchaseCreditNote)
                .WithMany(note => note.Lines)
                .HasForeignKey(line => new { line.PurchaseCreditNoteId, line.TenantId })
                .HasPrincipalKey(note => new { note.Id, note.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.GoodsReceiptLine)
                .WithMany()
                .HasForeignKey(line => line.GoodsReceiptLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesCreditNote>(entity =>
        {
            entity.HasQueryFilter(note => note.TenantId == CurrentTenantId);
            entity.Property(note => note.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(note => note.CreditNoteNumber).HasMaxLength(128).IsRequired();
            entity.Property(note => note.Reason).HasMaxLength(500).IsRequired();
            entity.Property(note => note.Notes).HasMaxLength(500);
            entity.Property(note => note.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(note => note.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(note => note.PostedBy).HasMaxLength(256).IsRequired();
            entity.Property(note => note.TotalValue).HasColumnType("decimal(18,6)");
            entity.HasAlternateKey(note => new { note.Id, note.TenantId });
            entity.HasIndex(note => new { note.TenantId, note.CompanyId, note.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_SalesCreditNotes_RequestKey");
            entity.HasIndex(note => new { note.TenantId, note.CompanyId, note.CreditNoteNumber })
                .IsUnique()
                .HasDatabaseName("UX_SalesCreditNotes_Number");
            entity.HasOne(note => note.Customer)
                .WithMany()
                .HasForeignKey(note => note.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(note => new { note.CompanyId, note.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesCreditNoteLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Disposition).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(line => line.CreditValue).HasColumnType("decimal(18,6)");
            entity.ToTable("SalesCreditNoteLines", table => table.HasCheckConstraint(
                "CK_SalesCreditNoteLines_Quantity",
                "\"Quantity\" > 0 AND \"CreditValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.SalesCreditNoteId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.TenantId, line.DeliveryNoteLineId });
            entity.HasOne(line => line.SalesCreditNote)
                .WithMany(note => note.Lines)
                .HasForeignKey(line => new { line.SalesCreditNoteId, line.TenantId })
                .HasPrincipalKey(note => new { note.Id, note.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.DeliveryNoteLine)
                .WithMany()
                .HasForeignKey(line => line.DeliveryNoteLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Quotation>(entity =>
        {
            entity.HasQueryFilter(quotation => quotation.TenantId == CurrentTenantId);
            entity.Property(quotation => quotation.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(quotation => quotation.QuotationNumber).HasMaxLength(50).IsRequired();
            entity.Property(quotation => quotation.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(quotation => quotation.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(quotation => quotation.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(quotation => quotation.Notes).HasMaxLength(500);
            entity.Property(quotation => quotation.CreatedBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(quotation => new { quotation.Id, quotation.TenantId });
            entity.HasIndex(quotation => new { quotation.TenantId, quotation.CompanyId, quotation.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_Quotations_RequestKey");
            entity.HasIndex(quotation => new { quotation.TenantId, quotation.CompanyId, quotation.QuotationNumber })
                .IsUnique();
            entity.HasOne(quotation => quotation.Customer)
                .WithMany()
                .HasForeignKey(quotation => quotation.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(quotation => new { quotation.CompanyId, quotation.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<QuotationLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.UnitPrice).HasColumnType("decimal(18,6)");
            entity.Property(line => line.NetValue).HasColumnType("decimal(18,6)");
            entity.ToTable("QuotationLines", table => table.HasCheckConstraint(
                "CK_QuotationLines_Values",
                "\"Quantity\" > 0 AND \"UnitPrice\" >= 0 AND \"NetValue\" >= 0"));
            entity.HasIndex(line => new { line.TenantId, line.QuotationId, line.LineNumber }).IsUnique();
            entity.HasOne(line => line.Quotation)
                .WithMany(quotation => quotation.Lines)
                .HasForeignKey(line => new { line.QuotationId, line.TenantId })
                .HasPrincipalKey(quotation => new { quotation.Id, quotation.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasQueryFilter(project => project.TenantId == CurrentTenantId);
            entity.Property(project => project.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(project => project.Code).HasMaxLength(32).IsRequired();
            entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
            entity.Property(project => project.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(project => project.Notes).HasMaxLength(500);
            entity.HasIndex(project => new { project.TenantId, project.CompanyId, project.Code }).IsUnique();
            entity.HasOne(project => project.Customer)
                .WithMany()
                .HasForeignKey(project => project.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(project => new { project.CompanyId, project.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.HasQueryFilter(task => task.TenantId == CurrentTenantId);
            entity.Property(task => task.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(task => task.Name).HasMaxLength(200).IsRequired();
            entity.Property(task => task.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(task => new { task.TenantId, task.ProjectId });
            entity.HasOne(task => task.Project)
                .WithMany()
                .HasForeignKey(task => new { task.ProjectId, task.TenantId })
                .HasPrincipalKey(project => new { project.Id, project.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(task => new { task.CompanyId, task.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TimesheetEntry>(entity =>
        {
            entity.HasQueryFilter(entry => entry.TenantId == CurrentTenantId);
            entity.Property(entry => entry.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.WorkerId).HasMaxLength(450).IsRequired();
            entity.Property(entry => entry.Hours).HasColumnType("decimal(5,2)");
            entity.Property(entry => entry.HourlyRate).HasColumnType("decimal(18,6)");
            entity.Property(entry => entry.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(entry => entry.ApprovedBy).HasMaxLength(256);
            entity.Property(entry => entry.RejectionReason).HasMaxLength(500);
            entity.Property(entry => entry.Notes).HasMaxLength(500);
            entity.ToTable("TimesheetEntries", table => table.HasCheckConstraint(
                "CK_TimesheetEntries_Hours",
                "\"Hours\" > 0 AND \"HourlyRate\" >= 0"));
            entity.HasIndex(entry => new { entry.TenantId, entry.ProjectId, entry.WorkDate });
            entity.HasOne(entry => entry.Project)
                .WithMany()
                .HasForeignKey(entry => new { entry.ProjectId, entry.TenantId })
                .HasPrincipalKey(project => new { project.Id, project.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.ProjectTask)
                .WithMany()
                .HasForeignKey(entry => new { entry.ProjectTaskId, entry.TenantId })
                .HasPrincipalKey(task => new { task.Id, task.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasQueryFilter(employee => employee.TenantId == CurrentTenantId);
            entity.Property(employee => employee.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(employee => employee.Code).HasMaxLength(32).IsRequired();
            entity.Property(employee => employee.FullName).HasMaxLength(200).IsRequired();
            entity.Property(employee => employee.Department).HasMaxLength(100);
            entity.Property(employee => employee.Position).HasMaxLength(100);
            entity.Property(employee => employee.UserId).HasMaxLength(450);
            entity.HasIndex(employee => new { employee.TenantId, employee.CompanyId, employee.Code }).IsUnique();
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(employee => new { employee.CompanyId, employee.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.HasQueryFilter(record => record.TenantId == CurrentTenantId);
            entity.Property(record => record.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(record => record.Notes).HasMaxLength(500);
            entity.HasIndex(record => new { record.TenantId, record.CompanyId, record.EmployeeId, record.WorkDate })
                .IsUnique()
                .HasFilter("\"CheckOutAtUtc\" IS NULL");
            entity.HasOne(record => record.Employee)
                .WithMany()
                .HasForeignKey(record => new { record.EmployeeId, record.TenantId })
                .HasPrincipalKey(employee => new { employee.Id, employee.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(record => new { record.CompanyId, record.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.HasQueryFilter(leave => leave.TenantId == CurrentTenantId);
            entity.Property(leave => leave.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(leave => leave.LeaveType).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(leave => leave.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(leave => leave.Reason).HasMaxLength(500).IsRequired();
            entity.Property(leave => leave.DecidedBy).HasMaxLength(256);
            entity.Property(leave => leave.DecisionReason).HasMaxLength(500);
            entity.ToTable("LeaveRequests", table => table.HasCheckConstraint(
                "CK_LeaveRequests_Range",
                "\"EndDate\" >= \"StartDate\" AND \"Days\" > 0"));
            entity.HasIndex(leave => new { leave.TenantId, leave.CompanyId, leave.EmployeeId, leave.Status });
            entity.HasOne(leave => leave.Employee)
                .WithMany()
                .HasForeignKey(leave => new { leave.EmployeeId, leave.TenantId })
                .HasPrincipalKey(employee => new { employee.Id, employee.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(leave => new { leave.CompanyId, leave.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalaryStructure>(entity =>
        {
            entity.HasQueryFilter(structure => structure.TenantId == CurrentTenantId);
            entity.Property(structure => structure.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(structure => structure.BaseSalary).HasColumnType("decimal(18,6)");
            entity.Property(structure => structure.Allowances).HasColumnType("decimal(18,6)");
            entity.HasIndex(structure => new { structure.TenantId, structure.CompanyId, structure.EmployeeId, structure.EffectiveFrom })
                .IsUnique();
            entity.HasOne(structure => structure.Employee)
                .WithMany()
                .HasForeignKey(structure => new { structure.EmployeeId, structure.TenantId })
                .HasPrincipalKey(employee => new { employee.Id, employee.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(structure => new { structure.CompanyId, structure.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayRun>(entity =>
        {
            entity.HasQueryFilter(run => run.TenantId == CurrentTenantId);
            entity.Property(run => run.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(run => run.TotalGross).HasColumnType("decimal(18,6)");
            entity.Property(run => run.TotalDeductions).HasColumnType("decimal(18,6)");
            entity.Property(run => run.TotalNet).HasColumnType("decimal(18,6)");
            entity.Property(run => run.RequestKey).HasMaxLength(200).IsRequired();
            entity.Property(run => run.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(run => run.PostedBy).HasMaxLength(256).IsRequired();
            entity.HasAlternateKey(run => new { run.Id, run.TenantId });
            entity.HasIndex(run => new { run.TenantId, run.CompanyId, run.Period })
                .IsUnique()
                .HasDatabaseName("UX_PayRuns_Period");
            entity.HasIndex(run => new { run.TenantId, run.CompanyId, run.RequestKey })
                .IsUnique()
                .HasDatabaseName("UX_PayRuns_RequestKey");
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(run => new { run.CompanyId, run.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payslip>(entity =>
        {
            entity.HasQueryFilter(slip => slip.TenantId == CurrentTenantId);
            entity.Property(slip => slip.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(slip => slip.BaseSalary).HasColumnType("decimal(18,6)");
            entity.Property(slip => slip.Allowances).HasColumnType("decimal(18,6)");
            entity.Property(slip => slip.Deductions).HasColumnType("decimal(18,6)");
            entity.Property(slip => slip.Gross).HasColumnType("decimal(18,6)");
            entity.Property(slip => slip.Net).HasColumnType("decimal(18,6)");
            entity.Property(slip => slip.Notes).HasMaxLength(500);
            entity.HasIndex(slip => new { slip.TenantId, slip.PayRunId, slip.EmployeeId }).IsUnique();
            entity.HasOne(slip => slip.PayRun)
                .WithMany(run => run.Payslips)
                .HasForeignKey(slip => new { slip.PayRunId, slip.TenantId })
                .HasPrincipalKey(run => new { run.Id, run.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(slip => slip.Employee)
                .WithMany()
                .HasForeignKey(slip => new { slip.EmployeeId, slip.TenantId })
                .HasPrincipalKey(employee => new { employee.Id, employee.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(slip => new { slip.CompanyId, slip.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillOfMaterials>(entity =>
        {
            entity.HasQueryFilter(bom => bom.TenantId == CurrentTenantId);
            entity.Property(bom => bom.TenantId).HasMaxLength(64).IsRequired();
            entity.HasAlternateKey(bom => new { bom.Id, bom.TenantId });
            entity.HasIndex(bom => new { bom.TenantId, bom.CompanyId, bom.ItemId, bom.Version })
                .IsUnique()
                .HasDatabaseName("UX_BillOfMaterials_ItemVersion");
            entity.HasOne(bom => bom.Item)
                .WithMany()
                .HasForeignKey(bom => bom.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(bom => new { bom.CompanyId, bom.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillOfMaterialsLine>(entity =>
        {
            entity.HasQueryFilter(line => line.TenantId == CurrentTenantId);
            entity.Property(line => line.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(line => line.QuantityPerUnit).HasColumnType("decimal(18,6)");
            entity.ToTable("BillOfMaterialsLines", table => table.HasCheckConstraint(
                "CK_BillOfMaterialsLines_Quantity",
                "\"QuantityPerUnit\" > 0"));
            entity.HasIndex(line => new { line.TenantId, line.BillOfMaterialsId, line.LineNumber }).IsUnique();
            entity.HasOne(line => line.BillOfMaterials)
                .WithMany(bom => bom.Lines)
                .HasForeignKey(line => new { line.BillOfMaterialsId, line.TenantId })
                .HasPrincipalKey(bom => new { bom.Id, bom.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.ComponentItem)
                .WithMany()
                .HasForeignKey(line => line.ComponentItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.HasQueryFilter(workOrder => workOrder.TenantId == CurrentTenantId);
            entity.Property(workOrder => workOrder.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(workOrder => workOrder.WorkOrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(workOrder => workOrder.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(workOrder => workOrder.Notes).HasMaxLength(500);
            entity.Property(workOrder => workOrder.ConsumedCost).HasColumnType("decimal(18,6)");
            entity.Property(workOrder => workOrder.CompletedBy).HasMaxLength(256);
            entity.HasAlternateKey(workOrder => new { workOrder.Id, workOrder.TenantId });
            entity.HasIndex(workOrder => new { workOrder.TenantId, workOrder.CompanyId, workOrder.WorkOrderNumber })
                .IsUnique();
            entity.HasIndex(workOrder => new { workOrder.TenantId, workOrder.CompanyId, workOrder.Status });
            entity.HasOne(workOrder => workOrder.BillOfMaterials)
                .WithMany()
                .HasForeignKey(workOrder => workOrder.BillOfMaterialsId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(workOrder => workOrder.Location)
                .WithMany()
                .HasForeignKey(workOrder => new { workOrder.LocationId, workOrder.TenantId })
                .HasPrincipalKey(location => new { location.Id, location.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(workOrder => new { workOrder.CompanyId, workOrder.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderConsumption>(entity =>
        {
            entity.HasQueryFilter(consumption => consumption.TenantId == CurrentTenantId);
            entity.Property(consumption => consumption.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(consumption => consumption.ConsumedValue).HasColumnType("decimal(18,6)");
            entity.ToTable("WorkOrderConsumptions", table => table.HasCheckConstraint(
                "CK_WorkOrderConsumptions_Quantity",
                "\"Quantity\" > 0 AND \"ConsumedValue\" >= 0"));
            entity.HasIndex(consumption => new { consumption.TenantId, consumption.WorkOrderId, consumption.ComponentItemId })
                .IsUnique();
            entity.HasIndex(consumption => new { consumption.TenantId, consumption.StockTransactionId }).IsUnique();
            entity.HasOne(consumption => consumption.WorkOrder)
                .WithMany(workOrder => workOrder.Consumptions)
                .HasForeignKey(consumption => new { consumption.WorkOrderId, consumption.TenantId })
                .HasPrincipalKey(workOrder => new { workOrder.Id, workOrder.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(consumption => new { consumption.CompanyId, consumption.TenantId })
                .HasPrincipalKey(company => new { company.Id, company.TenantId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExchangeRate>(entity =>
        {
            entity.ToTable("ExchangeRates");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.FromCurrency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.ToCurrency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Rate).HasColumnType("decimal(18,6)");
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.FromCurrency, e.ToCurrency, e.EffectiveDate }).IsUnique();
        });

        modelBuilder.Entity<FxRevaluationRun>(entity =>
        {
            entity.ToTable("FxRevaluationRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TotalUnrealizedGain).HasColumnType("decimal(18,4)");
            entity.Property(e => e.TotalUnrealizedLoss).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.FiscalPeriodId, e.Status });
        });

        modelBuilder.Entity<IntercompanyRelationship>(entity =>
        {
            entity.ToTable("IntercompanyRelationships");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.ParentCompanyId, e.SubsidiaryCompanyId }).IsUnique();
        });

        modelBuilder.Entity<IntercompanyTransaction>(entity =>
        {
            entity.ToTable("IntercompanyTransactions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ReferenceKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Amount).HasColumnType("decimal(18,4)");
            entity.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.ReferenceKey }).IsUnique();
        });

        modelBuilder.Entity<PortalContact>(entity =>
        {
            entity.ToTable("PortalContacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ContactName).HasMaxLength(256).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.PortalType, e.Email }).IsUnique();
        });

        modelBuilder.Entity<SupplierCollaborationEntry>(entity =>
        {
            entity.ToTable("SupplierCollaborationEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.InvoiceAmount).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.SupplierId, e.PurchaseOrderId });
        });

        modelBuilder.Entity<PriceList>(entity =>
        {
            entity.ToTable("PriceLists");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.Name }).IsUnique();
        });

        modelBuilder.Entity<ItemPrice>(entity =>
        {
            entity.ToTable("ItemPrices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Rate).HasColumnType("decimal(18,4)");
            entity.Property(e => e.MinQuantity).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.PriceListId, e.ItemId });
        });

        modelBuilder.Entity<PricingRule>(entity =>
        {
            entity.ToTable("PricingRules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(128).IsRequired();
            entity.Property(e => e.DiscountPercentage).HasColumnType("decimal(18,4)");
            entity.Property(e => e.DiscountAmount).HasColumnType("decimal(18,4)");
            entity.Property(e => e.MinQuantity).HasColumnType("decimal(18,4)");
            entity.Property(e => e.MinAmount).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.RuleType });
        });

        modelBuilder.Entity<LandedCostVoucher>(entity =>
        {
            entity.ToTable("LandedCostVouchers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.VoucherNumber).HasMaxLength(32).IsRequired();
            entity.Property(e => e.TotalAdditionalCharges).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.VoucherNumber }).IsUnique();
        });

        modelBuilder.Entity<LandedCostReceipt>(entity =>
        {
            entity.ToTable("LandedCostReceipts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.LandedCostVoucherId, e.GoodsReceiptId });
        });

        modelBuilder.Entity<LandedCostCharge>(entity =>
        {
            entity.ToTable("LandedCostCharges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Amount).HasColumnType("decimal(18,4)");
            entity.Property(e => e.Description).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<LandedCostItemAllocation>(entity =>
        {
            entity.ToTable("LandedCostItemAllocations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ApplicableCharges).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.LandedCostVoucherId, e.GoodsReceiptLineId });
        });

        modelBuilder.Entity<MaterialRequest>(entity =>
        {
            entity.ToTable("MaterialRequests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RequestNumber).HasMaxLength(32).IsRequired();
            entity.Property(e => e.RequestedBy).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.RequestNumber }).IsUnique();
        });

        modelBuilder.Entity<MaterialRequestLine>(entity =>
        {
            entity.ToTable("MaterialRequestLines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.MaterialRequestId, e.ItemId });
        });

        modelBuilder.Entity<SupplierQuotation>(entity =>
        {
            entity.ToTable("SupplierQuotations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.QuotationNumber).HasMaxLength(32).IsRequired();
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,4)");
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CompanyId, e.QuotationNumber }).IsUnique();
        });

        modelBuilder.Entity<SupplierQuotationLine>(entity =>
        {
            entity.ToTable("SupplierQuotationLines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Rate).HasColumnType("decimal(18,4)");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18,4)");
            entity.HasIndex(e => new { e.TenantId, e.SupplierQuotationId, e.ItemId });
        });
    }
}

/// <summary>
/// Internal helper that captures per-property old / new values during the audit pipeline
/// and converts them into <see cref="AuditLog"/> rows.
/// </summary>
public class AuditEntry
{
    public AuditEntry(EntityEntry entry)
    {
        Entry = entry;
    }

    public EntityEntry Entry { get; }
    public string UserId { get; set; } = null!;
    public string TableName { get; set; } = null!;
    public string Action { get; set; } = null!;
    public Dictionary<string, object> KeyValues { get; } = new();
    public Dictionary<string, object> OldValues { get; } = new();
    public Dictionary<string, object> NewValues { get; } = new();
    public List<PropertyEntry> TemporaryProperties { get; } = new();
    public List<string> ChangedColumns { get; } = new();

    public AuditLog ToAuditLog()
    {
        return new AuditLog
        {
            TenantId = Entry.Entity is ITenantScoped tenant ? tenant.TenantId : "default",
            EntityName = TableName,
            Action = Action,
            Username = UserId,
            Timestamp = DateTime.UtcNow,
            KeyValues = KeyValues.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(KeyValues),
            OldValues = OldValues.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(OldValues),
            NewValues = NewValues.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(NewValues),
            ChangedColumns = ChangedColumns.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(ChangedColumns)
        };
    }
}
