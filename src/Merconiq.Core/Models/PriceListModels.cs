using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

public sealed record CreatePriceListRequest(
    int CompanyId,
    string Name,
    string Currency,
    PriceListType PriceListType);

public sealed record SetItemPriceRequest(
    int PriceListId,
    int ItemId,
    decimal Rate,
    decimal MinQuantity = 1m,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

public sealed record CreatePricingRuleRequest(
    int CompanyId,
    string Title,
    PriceListType RuleType,
    int? ItemId = null,
    int? PartyId = null,
    decimal MinQuantity = 0m,
    decimal MinAmount = 0m,
    decimal DiscountPercentage = 0m,
    decimal DiscountAmount = 0m,
    int Priority = 1,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null);

public sealed record ResolveRateRequest(
    int CompanyId,
    int ItemId,
    PriceListType Type,
    string Currency = "USD",
    decimal Quantity = 1m,
    int? PartyId = null,
    DateOnly? TransactionDate = null);

public sealed record ResolvedRateResult(
    int ItemId,
    decimal BaseRate,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal FinalRate,
    string? AppliedPriceListName,
    string? AppliedPricingRuleTitle);
