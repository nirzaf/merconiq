using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class SerialNumberService(
    InventoryDbContext context,
    ILogger<SerialNumberService> logger) : ISerialNumberService
{
    public async Task<IReadOnlyList<SerialNumber>> RegisterSerialsAsync(
        RegisterSerialRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (request.SerialNumbers.Count == 0)
            throw new FinancePostingValidationException("At least one serial number is required.");

        var existing = await context.SerialNumbers
            .Where(s => s.CompanyId == request.CompanyId &&
                        s.ItemId == request.ItemId &&
                        request.SerialNumbers.Contains(s.SerialNo) &&
                        s.Status == SerialNumberStatus.InStock)
            .Select(s => s.SerialNo)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
            throw new FinancePostingConflictException($"Serial numbers already in stock: {string.Join(", ", existing)}");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warrantyDate = request.WarrantyMonths.HasValue
            ? today.AddMonths(request.WarrantyMonths.Value)
            : (DateOnly?)null;

        var results = new List<SerialNumber>();
        foreach (var sn in request.SerialNumbers.Distinct())
        {
            var entity = new SerialNumber
            {
                CompanyId = request.CompanyId,
                ItemId = request.ItemId,
                SerialNo = sn.Trim().ToUpperInvariant(),
                Status = SerialNumberStatus.InStock,
                LocationId = request.LocationId,
                GoodsReceiptLineId = request.GoodsReceiptLineId,
                WarrantyExpiryDate = warrantyDate
            };
            context.SerialNumbers.Add(entity);
            results.Add(entity);
        }

        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Registered {Count} serial numbers for Item {ItemId}", results.Count, request.ItemId);
        return results;
    }

    public async Task<IReadOnlyList<SerialNumber>> DeliverSerialsAsync(
        DeliverSerialRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var serials = await context.SerialNumbers
            .Where(s => s.CompanyId == request.CompanyId &&
                        s.ItemId == request.ItemId &&
                        request.SerialNumbers.Contains(s.SerialNo))
            .ToListAsync(cancellationToken);

        if (serials.Count != request.SerialNumbers.Count)
            throw new FinancePostingValidationException("One or more serial numbers not found.");

        foreach (var sn in serials)
        {
            if (sn.Status != SerialNumberStatus.InStock)
                throw new FinancePostingValidationException($"Serial {sn.SerialNo} is not in stock (current status: {sn.Status}).");
            if (sn.LocationId != request.LocationId)
                throw new FinancePostingValidationException($"Serial {sn.SerialNo} is in a different location.");

            sn.Status = SerialNumberStatus.Delivered;
            sn.LocationId = null;
            sn.CustomerId = request.CustomerId;
            sn.DeliveryNoteLineId = request.DeliveryNoteLineId;
        }

        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Delivered {Count} serials to Customer {CustomerId}", serials.Count, request.CustomerId);
        return serials;
    }

    public async Task<SerialHistoryResult?> GetSerialHistoryAsync(
        int companyId,
        int itemId,
        string serialNo,
        CancellationToken cancellationToken = default)
    {
        var sn = await context.SerialNumbers
            .FirstOrDefaultAsync(s => s.CompanyId == companyId &&
                                      s.ItemId == itemId &&
                                      s.SerialNo == serialNo.Trim().ToUpperInvariant(), cancellationToken);
        if (sn == null) return null;

        return new SerialHistoryResult(
            sn.SerialNo,
            sn.ItemId,
            sn.Status,
            sn.LocationId,
            sn.WarrantyExpiryDate,
            sn.CustomerId);
    }
}
