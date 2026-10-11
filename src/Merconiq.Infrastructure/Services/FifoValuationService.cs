using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class FifoValuationService(
    InventoryDbContext context,
    ILogger<FifoValuationService> logger) : IFifoValuationService
{
    public async Task<StockFifoLayer> RecordInwardLayerAsync(
        int companyId,
        int itemId,
        int locationId,
        int quantity,
        decimal unitCost,
        string? sourceReference,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (companyId <= 0 || itemId <= 0 || locationId <= 0)
            throw new FinancePostingValidationException("CompanyId, ItemId, and LocationId are required.");
        if (quantity <= 0)
            throw new FinancePostingValidationException("Inward quantity must be positive.");
        if (unitCost < 0)
            throw new FinancePostingValidationException("Unit cost cannot be negative.");

        var layer = new StockFifoLayer
        {
            CompanyId = companyId,
            ItemId = itemId,
            LocationId = locationId,
            InwardDateUtc = DateTime.UtcNow,
            RemainingQuantity = quantity,
            UnitCost = unitCost,
            SourceReference = sourceReference
        };

        context.StockFifoLayers.Add(layer);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Recorded FIFO inward layer {Id} with {Qty} units at {UnitCost}", layer.Id, quantity, unitCost);
        return layer;
    }

    public async Task<FifoConsumptionResult> ConsumeFifoLayersAsync(
        int companyId,
        int itemId,
        int locationId,
        int quantity,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            throw new FinancePostingValidationException("Quantity to consume must be positive.");

        var availableLayers = await context.StockFifoLayers
            .Where(l => l.CompanyId == companyId &&
                        l.ItemId == itemId &&
                        l.LocationId == locationId &&
                        l.RemainingQuantity > 0)
            .OrderBy(l => l.InwardDateUtc)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        int totalAvailable = availableLayers.Sum(l => l.RemainingQuantity);
        if (totalAvailable < quantity)
            throw new FinancePostingValidationException($"Insufficient stock in FIFO layers. Requested: {quantity}, Available: {totalAvailable}");

        int remainingToConsume = quantity;
        decimal totalCost = 0m;
        var consumedDetails = new List<ConsumedLayerDetail>();

        foreach (var layer in availableLayers)
        {
            if (remainingToConsume <= 0) break;

            int take = Math.Min(layer.RemainingQuantity, remainingToConsume);
            layer.RemainingQuantity -= take;
            remainingToConsume -= take;

            decimal layerCost = take * layer.UnitCost;
            totalCost += layerCost;

            consumedDetails.Add(new ConsumedLayerDetail(
                layer.Id,
                take,
                layer.UnitCost,
                layerCost));
        }

        await context.SaveChangesAsAsync(actor, cancellationToken);
        decimal avgUnitCost = quantity > 0 ? Math.Round(totalCost / quantity, 4) : 0m;
        logger.LogInformation("Consumed {Qty} units via FIFO for total cost {Cost}", quantity, totalCost);

        return new FifoConsumptionResult(totalCost, avgUnitCost, consumedDetails);
    }
}
