using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

public interface IForeignCurrencyService
{
    Task<ExchangeRateView> SetExchangeRateAsync(
        SetExchangeRateRequest request,
        CancellationToken cancellationToken = default);

    Task<ExchangeRateView?> GetExchangeRateAsync(
        int companyId,
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<ConvertedCurrencyResult> ConvertAmountAsync(
        ConvertCurrencyRequest request,
        CancellationToken cancellationToken = default);

    decimal CalculateRealizedGainLoss(
        decimal originalBaseAmount,
        decimal settledBaseAmount);

    Task<FxRevaluationResult> RunUnrealizedRevaluationAsync(
        RunFxRevaluationRequest request,
        CancellationToken cancellationToken = default);

    Task ReverseUnrealizedRevaluationAsync(
        int runId,
        CancellationToken cancellationToken = default);
}
