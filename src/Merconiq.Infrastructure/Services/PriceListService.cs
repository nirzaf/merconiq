using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

public sealed class PriceListService(
    InventoryDbContext context,
    ILogger<PriceListService> logger) : IPriceListService
{
    public async Task<PriceList> CreatePriceListAsync(
        CreatePriceListRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new FinancePostingValidationException("PriceList Name is required.");

        var priceList = new PriceList
        {
            CompanyId = request.CompanyId,
            Name = request.Name.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
            PriceListType = request.PriceListType,
            IsActive = true
        };

        context.PriceLists.Add(priceList);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        logger.LogInformation("Created PriceList {Name} for Company {CompanyId}", priceList.Name, priceList.CompanyId);
        return priceList;
    }

    public async Task<ItemPrice> SetItemPriceAsync(
        SetItemPriceRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PriceListId <= 0)
            throw new FinancePostingValidationException("A valid PriceListId is required.");
        if (request.ItemId <= 0)
            throw new FinancePostingValidationException("A valid ItemId is required.");
        if (request.Rate < 0)
            throw new FinancePostingValidationException("Price rate cannot be negative.");

        var priceList = await context.PriceLists.FindAsync([request.PriceListId], cancellationToken)
            ?? throw new FinancePostingValidationException("PriceList not found.");

        var item = await context.Items.FindAsync([request.ItemId], cancellationToken)
            ?? throw new FinancePostingValidationException("Item not found.");

        var itemPrice = new ItemPrice
        {
            PriceListId = request.PriceListId,
            ItemId = request.ItemId,
            Rate = request.Rate,
            MinQuantity = request.MinQuantity > 0 ? request.MinQuantity : 1m,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        context.ItemPrices.Add(itemPrice);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return itemPrice;
    }

    public async Task<PricingRule> CreatePricingRuleAsync(
        CreatePricingRuleRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new FinancePostingValidationException("Pricing rule title is required.");

        var rule = new PricingRule
        {
            CompanyId = request.CompanyId,
            Title = request.Title.Trim(),
            RuleType = request.RuleType,
            ItemId = request.ItemId,
            PartyId = request.PartyId,
            MinQuantity = request.MinQuantity,
            MinAmount = request.MinAmount,
            DiscountPercentage = request.DiscountPercentage,
            DiscountAmount = request.DiscountAmount,
            Priority = request.Priority,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true
        };

        context.PricingRules.Add(rule);
        await context.SaveChangesAsAsync(actor, cancellationToken);
        return rule;
    }

    public async Task<ResolvedRateResult> ResolveRateAsync(
        ResolveRateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId <= 0)
            throw new FinancePostingValidationException("A valid CompanyId is required.");
        if (request.ItemId <= 0)
            throw new FinancePostingValidationException("A valid ItemId is required.");

        var today = request.TransactionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var targetCurrency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();

        // 1. Check for active ItemPrice in a matching PriceList
        var matchedItemPrice = await context.ItemPrices
            .Include(ip => ip.PriceList)
            .Where(ip => ip.ItemId == request.ItemId &&
                         ip.IsActive &&
                         ip.PriceList.CompanyId == request.CompanyId &&
                         ip.PriceList.PriceListType == request.Type &&
                         ip.PriceList.Currency == targetCurrency &&
                         ip.PriceList.IsActive &&
                         ip.MinQuantity <= request.Quantity &&
                         (!ip.ValidFrom.HasValue || ip.ValidFrom.Value <= today) &&
                         (!ip.ValidTo.HasValue || ip.ValidTo.Value >= today))
            .OrderByDescending(ip => ip.MinQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        decimal baseRate;
        string? appliedPriceListName = null;

        if (matchedItemPrice != null)
        {
            baseRate = matchedItemPrice.Rate;
            appliedPriceListName = matchedItemPrice.PriceList.Name;
        }
        else
        {
            var item = await context.Items.FindAsync([request.ItemId], cancellationToken)
                ?? throw new FinancePostingValidationException($"Item {request.ItemId} not found.");
            baseRate = item.Rate;
        }

        // 2. Evaluate active PricingRules
        var lineAmount = baseRate * request.Quantity;
        var applicableRules = await context.PricingRules
            .Where(pr => pr.CompanyId == request.CompanyId &&
                         pr.RuleType == request.Type &&
                         pr.IsActive &&
                         (!pr.ItemId.HasValue || pr.ItemId.Value == request.ItemId) &&
                         (!pr.PartyId.HasValue || pr.PartyId.Value == request.PartyId) &&
                         pr.MinQuantity <= request.Quantity &&
                         pr.MinAmount <= lineAmount &&
                         (!pr.ValidFrom.HasValue || pr.ValidFrom.Value <= today) &&
                         (!pr.ValidTo.HasValue || pr.ValidTo.Value >= today))
            .OrderByDescending(pr => pr.Priority)
            .ThenByDescending(pr => pr.DiscountPercentage)
            .ToListAsync(cancellationToken);

        var bestRule = applicableRules.FirstOrDefault();

        decimal discountPercentage = 0m;
        decimal discountAmount = 0m;
        string? ruleTitle = null;

        if (bestRule != null)
        {
            ruleTitle = bestRule.Title;
            if (bestRule.DiscountPercentage > 0)
            {
                discountPercentage = bestRule.DiscountPercentage;
                discountAmount = Math.Round(baseRate * (discountPercentage / 100m), 4);
            }
            else if (bestRule.DiscountAmount > 0)
            {
                discountAmount = bestRule.DiscountAmount;
                discountPercentage = baseRate > 0 ? Math.Round((discountAmount / baseRate) * 100m, 2) : 0m;
            }
        }

        var finalRate = Math.Max(0m, baseRate - discountAmount);

        return new ResolvedRateResult(
            request.ItemId,
            baseRate,
            discountPercentage,
            discountAmount,
            finalRate,
            appliedPriceListName,
            ruleTitle);
    }
}
