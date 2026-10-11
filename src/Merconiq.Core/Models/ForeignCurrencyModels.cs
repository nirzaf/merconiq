namespace Merconiq.Core.Models;

public sealed record SetExchangeRateRequest(
    int CompanyId,
    string FromCurrency,
    string ToCurrency,
    DateOnly EffectiveDate,
    decimal Rate);

public sealed record ExchangeRateView(
    int Id,
    int CompanyId,
    string FromCurrency,
    string ToCurrency,
    DateOnly EffectiveDate,
    decimal Rate);

public sealed record ConvertCurrencyRequest(
    int CompanyId,
    decimal Amount,
    string FromCurrency,
    string ToCurrency,
    DateOnly Date);

public sealed record ConvertedCurrencyResult(
    decimal SourceAmount,
    string SourceCurrency,
    decimal ConvertedAmount,
    string TargetCurrency,
    decimal AppliedRate,
    DateOnly RateDate);

public sealed record RunFxRevaluationRequest(
    int CompanyId,
    DateOnly RevaluationDate,
    int FiscalPeriodId,
    int UnrealizedGainAccountId,
    int UnrealizedLossAccountId);

public sealed record FxRevaluationResult(
    int RunId,
    int CompanyId,
    DateOnly RevaluationDate,
    decimal TotalGain,
    decimal TotalLoss,
    int? JournalEntryId,
    string Status);
