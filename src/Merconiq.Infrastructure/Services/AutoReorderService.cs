using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class AutoReorderService(
    InventoryDbContext context,
    IProcurementRequisitionService requisitionService,
    ILogger<AutoReorderService> logger) : IAutoReorderService
{
    public async Task<ItemReorderRule> SetReorderRuleAsync(
        SetReorderRuleRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0 || request.ItemId <= 0 || request.LocationId <= 0)
            throw new FinancePostingValidationException("CompanyId, ItemId, and LocationId are required.");
        if (request.ReorderLevel < 0 || request.ReorderQuantity <= 0)
            throw new FinancePostingValidationException("ReorderLevel cannot be negative and ReorderQuantity must be positive.");

        var rule = await context.ItemReorderRules
            .FirstOrDefaultAsync(r => r.CompanyId == request.CompanyId &&
                                      r.ItemId == request.ItemId &&
                                      r.LocationId == request.LocationId, cancellationToken);

        if (rule == null)
        {
            rule = new ItemReorderRule
            {
                CompanyId = request.CompanyId,
                ItemId = request.ItemId,
                LocationId = request.LocationId,
                ReorderLevel = request.ReorderLevel,
                ReorderQuantity = request.ReorderQuantity,
                IsActive = true
            };
            context.ItemReorderRules.Add(rule);
        }
        else
        {
            rule.ReorderLevel = request.ReorderLevel;
            rule.ReorderQuantity = request.ReorderQuantity;
            rule.IsActive = true;
        }

        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Saved ReorderRule for Item {ItemId} at Location {LocationId}", request.ItemId, request.LocationId);
        return rule;
    }

    public async Task<AutoReorderTriggerSummary> EvaluateReorderTriggersAsync(
        int companyId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");

        var activeRules = await context.ItemReorderRules
            .Where(r => r.CompanyId == companyId && r.IsActive)
            .ToListAsync(cancellationToken);

        var createdRequisitions = new List<MaterialRequestResult>();
        int triggeredCount = 0;

        foreach (var rule in activeRules)
        {
            var currentStock = await context.StockInHand
                .Where(s => s.ItemId == rule.ItemId && s.LocationId == rule.LocationId)
                .SumAsync(s => (int?)s.Quantity, cancellationToken) ?? 0;

            if (currentStock <= rule.ReorderLevel)
            {
                triggeredCount++;
                var reqResult = await requisitionService.CreateMaterialRequestAsync(
                    new CreateMaterialRequest(
                        companyId,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                        MaterialRequestPurpose.Purchase,
                        "AutoReorderEngine",
                        [new MaterialRequestLineInput(rule.ItemId, rule.ReorderQuantity)],
                        $"Automated reorder: current stock {currentStock} <= reorder level {rule.ReorderLevel}"),
                    actor,
                    cancellationToken);

                createdRequisitions.Add(reqResult);
            }
        }

        logger.LogInformation("Evaluated {Total} reorder rules, triggered {Triggered}", activeRules.Count, triggeredCount);
        return new AutoReorderTriggerSummary(activeRules.Count, triggeredCount, createdRequisitions);
    }
}
