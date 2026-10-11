using System.Globalization;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Manufacturing. Work-order completion consumes each BOM component through the stock ledger at
/// moving-average cost, receives the finished quantity at the exact consumed unit cost, and
/// posts the paired journals (Dr WIP / Cr Inventory on consumption; Dr Inventory / Cr WIP on
/// completion) through the finance kernel — all in one transaction, exactly once. Cost
/// conservation is exact: the finished value equals the consumed value to the last decimal.
/// </summary>
public sealed class ManufacturingService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IStockService stockService,
    IJournalPostingService journalPosting,
    IAccountPostingPolicy postingPolicy,
    IFiscalCalendarService fiscalCalendar,
    IDocumentNumberService documentNumbers,
    ILogger<ManufacturingService> logger) : IManufacturingService
{
    /// <summary>Unique BOM version index; duplicate versions classify against this name.</summary>
    public const string BomVersionIndex = "UX_BillOfMaterials_ItemVersion";

    private const int MaximumBomLines = 100;
    private const decimal MaximumQuantityPerUnit = 1_000_000m;

    /// <inheritdoc />
    public async Task<BillOfMaterials> CreateBillOfMaterialsAsync(
        CreateBomRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > MaximumBomLines)
            throw new FinancePostingValidationException(
                $"A BOM requires between 1 and {MaximumBomLines} component lines.");
        if (request.Version < 1)
            throw new FinancePostingValidationException("The BOM version must be at least 1.");

        BillOfMaterials created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"manufacturing:{request.CompanyId}", cancellationToken);
            if (!await context.Companies.AnyAsync(c => c.Id == request.CompanyId && c.IsActive, cancellationToken))
                throw new FinancePostingValidationException("Active company not found.");

            var itemIds = request.Lines.Select(line => line.ComponentItemId)
                .Append(request.ItemId).Distinct().ToList();
            var items = await context.Items
                .Where(item => itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (!items.ContainsKey(request.ItemId))
                throw new FinancePostingValidationException("The finished item does not exist.");

            // A component that equals the finished item would make consumption self-referential.
            if (request.Lines.Any(line => line.ComponentItemId == request.ItemId))
                throw new FinancePostingValidationException(
                    "The finished item cannot be a component of its own BOM.");
            foreach (var line in request.Lines)
            {
                if (line is null || !items.ContainsKey(line.ComponentItemId))
                    throw new FinancePostingValidationException(
                        "Every BOM line needs an existing component item.");
                if (line.QuantityPerUnit <= 0 || line.QuantityPerUnit > MaximumQuantityPerUnit ||
                    line.QuantityPerUnit != decimal.Truncate(line.QuantityPerUnit))
                    throw new FinancePostingValidationException(
                        "Component quantities per unit must be positive whole base units.");
            }

            if (await context.BillOfMaterials.AnyAsync(
                    bom => bom.CompanyId == request.CompanyId &&
                        bom.ItemId == request.ItemId &&
                        bom.Version == request.Version,
                    cancellationToken))
                throw new FinancePostingConflictException(
                    $"BOM version {request.Version} for this item already exists in this company.");

            created = new BillOfMaterials
            {
                CompanyId = request.CompanyId,
                ItemId = request.ItemId,
                Version = request.Version,
                IsActive = true
            };
            var lineNumber = 1;
            foreach (var line in request.Lines)
            {
                created.Lines.Add(new BillOfMaterialsLine
                {
                    CompanyId = request.CompanyId,
                    ComponentItemId = line.ComponentItemId,
                    QuantityPerUnit = line.QuantityPerUnit,
                    LineNumber = lineNumber++
                });
            }
            context.BillOfMaterials.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        logger.LogInformation(
            "BOM version {Version} created for item {ItemId} in company {CompanyId}",
            request.Version, request.ItemId, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<WorkOrder> CreateWorkOrderAsync(
        CreateWorkOrderRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (request.Quantity <= 0)
            throw new FinancePostingValidationException("The production quantity must be positive.");

        WorkOrder created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"manufacturing:{request.CompanyId}", cancellationToken);
            var bom = await context.BillOfMaterials
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.BillOfMaterialsId &&
                        candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("BOM not found in this company.");
            if (!bom.IsActive || bom.Lines.Count == 0)
                throw new FinancePostingValidationException(
                    "Work orders require an active BOM with component lines.");

            var workOrder = new WorkOrder
            {
                CompanyId = request.CompanyId,
                BillOfMaterialsId = bom.Id,
                ItemId = bom.ItemId,
                Quantity = request.Quantity,
                LocationId = request.LocationId,
                Status = WorkOrderStatus.Draft,
                BomVersion = bom.Version,
                Notes = request.Notes
            };
            context.WorkOrders.Add(workOrder);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            workOrder.WorkOrderNumber = await documentNumbers.AllocateAsync(
                request.CompanyId, "WorkOrder", DateTime.UtcNow.Year, "WO", cancellationToken);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = workOrder;
        }, cancellationToken: cancellationToken);
        logger.LogInformation(
            "Work order {WorkOrderNumber} created for {Quantity} units of item {ItemId}",
            created.WorkOrderNumber, created.Quantity, created.ItemId);
        return created;
    }

    /// <inheritdoc />
    public async Task<WorkOrder> ReleaseWorkOrderAsync(
        int companyId, int workOrderId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        WorkOrder released = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"manufacturing:{companyId}", cancellationToken);
            var workOrder = await FindWorkOrderAsync(companyId, workOrderId, cancellationToken);
            if (workOrder.Status != WorkOrderStatus.Draft)
                throw new FinancePostingConflictException(
                    $"A {workOrder.Status} work order cannot be released.");
            workOrder.Status = WorkOrderStatus.Released;
            workOrder.PostingDate = DateOnly.FromDateTime(DateTime.UtcNow);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            released = workOrder;
        }, cancellationToken: cancellationToken);
        return released;
    }

    /// <inheritdoc />
    public async Task<WorkOrder> CompleteWorkOrderAsync(
        int companyId, int workOrderId, StockMutationScope mutationScope, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        WorkOrder completed = null!;
        await unitOfWork.ExecuteInTransactionAsync(
            async () =>
            {
                await unitOfWork.AcquireTenantOperationLockAsync($"manufacturing:{companyId}", cancellationToken);
                var workOrder = await context.WorkOrders
                    .Include(candidate => candidate.Consumptions)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == workOrderId && candidate.CompanyId == companyId,
                        cancellationToken)
                    ?? throw new FinancePostingValidationException("Work order not found in this company.");

                if (workOrder.Status == WorkOrderStatus.Completed)
                {
                    // Idempotent: the committed journals answer repeated completion calls.
                    completed = workOrder;
                    return;
                }
                if (workOrder.Status != WorkOrderStatus.Released)
                    throw new FinancePostingConflictException(
                        $"A {workOrder.Status} work order cannot be completed; release it first.");

                var company = await context.Companies.SingleOrDefaultAsync(
                        candidate => candidate.Id == companyId && candidate.IsActive, cancellationToken)
                    ?? throw new FinancePostingValidationException("Active company not found.");
                var postingDate = workOrder.PostingDate;
                await fiscalCalendar.EnsurePeriodOpenForDateAsync(companyId, postingDate, cancellationToken);

                // Consume components exactly per the snapshotted BOM version.
                var bom = await context.BillOfMaterials
                    .Include(candidate => candidate.Lines)
                    .SingleAsync(
                        candidate => candidate.Id == workOrder.BillOfMaterialsId, cancellationToken);

                var wipAccount = await postingPolicy.ResolveAsync(
                    companyId, AccountPostingRole.WorkInProgress, cancellationToken);
                var inventoryAccount = await postingPolicy.ResolveAsync(
                    companyId, AccountPostingRole.Inventory, cancellationToken);
                var consumptionLines = new List<JournalLineInput>();
                var totalConsumed = 0m;

                foreach (var bomLine in bom.Lines.OrderBy(line => line.LineNumber))
                {
                    var quantity = checked((int)(bomLine.QuantityPerUnit * workOrder.Quantity));
                    if (quantity <= 0)
                        throw new FinancePostingValidationException(
                            "Component quantities must produce a positive consumption.");

                    // The issued stock transaction and its valuation entry are captured from the
                    // shared change tracker; the entry value is the exact consumed cost.
                    var knownTransactionIds = context.StockTransactions.Local.Select(t => t.Id).ToHashSet();
                    await stockService.SellStockAsync(
                        bomLine.ComponentItemId,
                        workOrder.LocationId,
                        quantity,
                        notes: $"Work order {workOrder.WorkOrderNumber} consumption",
                        mutationScope: mutationScope);
                    var issued = context.StockTransactions.Local
                        .Where(t => !knownTransactionIds.Contains(t.Id) &&
                            t.ItemId == bomLine.ComponentItemId &&
                            t.FromLocationId == workOrder.LocationId &&
                            t.Quantity == quantity)
                        .OrderByDescending(t => t.Id)
                        .FirstOrDefault()
                        ?? throw new FinancePostingValidationException(
                            "The stock ledger did not record the component issue; completion aborted.");
                    var issuedValue = (await context.StockValuationEntries
                        .Where(entry => entry.StockTransactionId == issued.Id)
                        .Select(entry => (decimal?)entry.TotalValue)
                        .FirstOrDefaultAsync(cancellationToken)) ?? 0m;

                    workOrder.Consumptions.Add(new WorkOrderConsumption
                    {
                        CompanyId = companyId,
                        ComponentItemId = bomLine.ComponentItemId,
                        Quantity = quantity,
                        StockTransactionId = issued.Id,
                        ConsumedValue = issuedValue
                    });
                    totalConsumed += issuedValue;
                    consumptionLines.Add(new JournalLineInput(
                        wipAccount.ChartOfAccountId,
                        $"WIP consumption {workOrder.WorkOrderNumber}",
                        issuedValue,
                        0m));
                    consumptionLines.Add(new JournalLineInput(
                        inventoryAccount.ChartOfAccountId,
                        $"Raw materials issued {workOrder.WorkOrderNumber}",
                        0m,
                        issuedValue));
                }

                // Receive the finished goods at the exact consumed unit cost so the finished
                // valuation equals the consumed value to the last decimal.
                var unitCost = decimal.Round(totalConsumed / workOrder.Quantity, 6, MidpointRounding.AwayFromZero);
                await stockService.ReceiveStockAsync(
                    workOrder.ItemId,
                    workOrder.LocationId,
                    workOrder.Quantity,
                    notes: $"Work order {workOrder.WorkOrderNumber} completion",
                    unitCost: unitCost,
                    mutationScope: mutationScope);

                // Paired WIP journals: consumption out of raw inventory into WIP, then into
                // finished inventory. WIP nets to zero; total value is conserved exactly.
                await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        companyId,
                        postingDate,
                        company.BaseCurrency,
                        $"Component consumption for {workOrder.WorkOrderNumber}",
                        "WorkOrder",
                        $"{workOrder.Id}-consume",
                        "Post",
                        1,
                        consumptionLines),
                    actor,
                    cancellationToken);
                var completionJournal = await journalPosting.PostAsync(
                    new JournalPostingRequest(
                        companyId,
                        postingDate,
                        company.BaseCurrency,
                        $"Finished goods for {workOrder.WorkOrderNumber}",
                        "WorkOrder",
                        $"{workOrder.Id}-complete",
                        "Post",
                        1,
                        [
                            new JournalLineInput(inventoryAccount.ChartOfAccountId, $"Finished goods {workOrder.WorkOrderNumber}", totalConsumed, 0m),
                            new JournalLineInput(wipAccount.ChartOfAccountId, $"WIP cleared {workOrder.WorkOrderNumber}", 0m, totalConsumed)
                        ]),
                    actor,
                    cancellationToken);

                workOrder.Status = WorkOrderStatus.Completed;
                workOrder.ConsumedCost = totalConsumed;
                workOrder.CompletedAtUtc = DateTime.UtcNow;
                workOrder.CompletedBy = actor.Trim();
                await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);

                logger.LogInformation(
                    "Work order {WorkOrderNumber} completed: consumed {Consumed}, finished value {Finished}, journal {Journal}",
                    workOrder.WorkOrderNumber, totalConsumed, totalConsumed, completionJournal.JournalNumber);
                completed = workOrder;
            },
            cancellationToken: cancellationToken,
            verifySucceeded: async () => await context.WorkOrders.AnyAsync(
                candidate => candidate.Id == workOrderId && candidate.Status == WorkOrderStatus.Completed,
                cancellationToken));

        return completed;
    }

    /// <inheritdoc />
    public async Task<MaterialProposal> GetMaterialProposalAsync(
        int companyId, int workOrderId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var workOrder = await context.WorkOrders
            .SingleOrDefaultAsync(
                candidate => candidate.Id == workOrderId && candidate.CompanyId == companyId,
                cancellationToken)
            ?? throw new FinancePostingValidationException("Work order not found in this company.");

        var bom = await context.BillOfMaterials
            .Include(candidate => candidate.Lines)
            .SingleAsync(candidate => candidate.Id == workOrder.BillOfMaterialsId, cancellationToken);
        var componentIds = bom.Lines.Select(line => line.ComponentItemId).ToList();
        var stock = await context.StockInHand
            .Where(position => position.TenantId == context.CurrentTenantId &&
                position.LocationId == workOrder.LocationId &&
                componentIds.Contains(position.ItemId))
            .GroupBy(position => position.ItemId)
            .Select(group => new
            {
                ItemId = group.Key,
                OnHand = group.Sum(position => (decimal)position.Quantity),
                Reserved = group.Sum(position => (decimal)position.ReservedQuantity),
                Quarantined = group.Sum(position => (decimal)position.QuarantinedQuantity)
            })
            .ToDictionaryAsync(row => row.ItemId, cancellationToken);

        var lines = bom.Lines.OrderBy(line => line.LineNumber)
            .Select(line =>
            {
                var required = line.QuantityPerUnit * workOrder.Quantity;
                stock.TryGetValue(line.ComponentItemId, out var position);
                var onHand = position?.OnHand ?? 0m;
                var reserved = position?.Reserved ?? 0m;
                var available = onHand - reserved;
                var shortage = required > available ? required - available : 0m;
                return new MaterialProposalLine(
                    line.ComponentItemId,
                    required,
                    onHand,
                    reserved,
                    available,
                    shortage,
                    shortage == 0m ? "Proceed" : "Shortage");
            })
            .ToList();
        return new MaterialProposal(
            workOrder.Id,
            workOrder.WorkOrderNumber,
            workOrder.Quantity,
            lines.All(line => line.ShortageQuantity == 0m),
            lines);
    }

    /// <inheritdoc />
    public Task<WorkOrder?> GetWorkOrderAsync(
        int companyId, int workOrderId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        return context.WorkOrders
            .Include(workOrder => workOrder.Consumptions)
            .SingleOrDefaultAsync(workOrder => workOrder.Id == workOrderId && workOrder.CompanyId == companyId,
                cancellationToken);
    }

    private Task<WorkOrder> FindWorkOrderAsync(
        int companyId, int workOrderId, CancellationToken cancellationToken) =>
        context.WorkOrders.SingleOrDefaultAsync(
            candidate => candidate.Id == workOrderId && candidate.CompanyId == companyId, cancellationToken)
        ?? throw new FinancePostingValidationException("Work order not found in this company.");
}
