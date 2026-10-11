using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IPriceListService
{
    Task<PriceList> CreatePriceListAsync(CreatePriceListRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ItemPrice> SetItemPriceAsync(SetItemPriceRequest request, string actor, CancellationToken cancellationToken = default);
    Task<PricingRule> CreatePricingRuleAsync(CreatePricingRuleRequest request, string actor, CancellationToken cancellationToken = default);
    Task<ResolvedRateResult> ResolveRateAsync(ResolveRateRequest request, CancellationToken cancellationToken = default);
}
