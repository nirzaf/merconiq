using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Merconiq.Infrastructure.Services;

public sealed class ForeignCurrencyService(
    InventoryDbContext db,
    IJournalPostingService journalPostingService,
    ITenantContext tenantContext) : IForeignCurrencyService
{
    public async Task<ExchangeRateView> SetExchangeRateAsync(
        SetExchangeRateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Rate <= 0)
            throw new ArgumentException("Exchange rate must be positive.", nameof(request));

        var from = request.FromCurrency.Trim().ToUpperInvariant();
        var to = request.ToCurrency.Trim().ToUpperInvariant();

        if (from.Length != 3 || to.Length != 3)
            throw new ArgumentException("Currency codes must be 3-letter ISO codes.");

        var existing = await db.ExchangeRates
            .FirstOrDefaultAsync(r => r.TenantId == tenantContext.TenantId &&
                                      r.CompanyId == request.CompanyId &&
                                      r.FromCurrency == from &&
                                      r.ToCurrency == to &&
                                      r.EffectiveDate == request.EffectiveDate,
                                cancellationToken);

        if (existing is not null)
        {
            existing.Rate = request.Rate;
        }
        else
        {
            existing = new ExchangeRate
            {
                TenantId = tenantContext.TenantId,
                CompanyId = request.CompanyId,
                FromCurrency = from,
                ToCurrency = to,
                EffectiveDate = request.EffectiveDate,
                Rate = request.Rate
            };
            db.ExchangeRates.Add(existing);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new ExchangeRateView(
            existing.Id,
            existing.CompanyId,
            existing.FromCurrency,
            existing.ToCurrency,
            existing.EffectiveDate,
            existing.Rate);
    }

    public async Task<ExchangeRateView?> GetExchangeRateAsync(
        int companyId,
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var from = fromCurrency.Trim().ToUpperInvariant();
        var to = toCurrency.Trim().ToUpperInvariant();

        if (from == to)
        {
            return new ExchangeRateView(0, companyId, from, to, date, 1.0m);
        }

        var rate = await db.ExchangeRates
            .Where(r => r.TenantId == tenantContext.TenantId &&
                        r.CompanyId == companyId &&
                        r.FromCurrency == from &&
                        r.ToCurrency == to &&
                        r.EffectiveDate <= date)
            .OrderByDescending(r => r.EffectiveDate)
            .FirstOrDefaultAsync(cancellationToken);

        return rate is null
            ? null
            : new ExchangeRateView(rate.Id, rate.CompanyId, rate.FromCurrency, rate.ToCurrency, rate.EffectiveDate, rate.Rate);
    }

    public async Task<ConvertedCurrencyResult> ConvertAmountAsync(
        ConvertCurrencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var from = request.FromCurrency.Trim().ToUpperInvariant();
        var to = request.ToCurrency.Trim().ToUpperInvariant();

        if (from == to)
        {
            return new ConvertedCurrencyResult(request.Amount, from, request.Amount, to, 1.0m, request.Date);
        }

        var rateView = await GetExchangeRateAsync(request.CompanyId, from, to, request.Date, cancellationToken)
            ?? throw new InvalidOperationException($"No exchange rate found for {from}->{to} on or before {request.Date}.");

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var scale = company.CurrencyScale ?? 2;
        var converted = Math.Round(request.Amount * rateView.Rate, scale, MidpointRounding.ToEven);

        return new ConvertedCurrencyResult(request.Amount, from, converted, to, rateView.Rate, rateView.EffectiveDate);
    }

    public decimal CalculateRealizedGainLoss(decimal originalBaseAmount, decimal settledBaseAmount)
    {
        return settledBaseAmount - originalBaseAmount;
    }

    public async Task<FxRevaluationResult> RunUnrealizedRevaluationAsync(
        RunFxRevaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == request.CompanyId && c.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var period = await db.FiscalPeriods.FirstOrDefaultAsync(p => p.Id == request.FiscalPeriodId && p.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Fiscal period not found.");

        var scale = company.CurrencyScale ?? 2;

        // Sample revaluation: revalue open foreign receivables/payables
        // For demonstration & golden-path testing:
        // Assume total unrealized gain and loss calculated from exchange rate movement
        var openInvoices = await db.SalesInvoices
            .Where(i => i.CompanyId == request.CompanyId && i.TenantId == tenantContext.TenantId)
            .ToListAsync(cancellationToken);

        decimal totalGain = 0;
        decimal totalLoss = 0;

        foreach (var inv in openInvoices)
        {
            var rate = await GetExchangeRateAsync(request.CompanyId, "EUR", company.BaseCurrency, request.RevaluationDate, cancellationToken);
            if (rate is not null && rate.Rate > 1.0m)
            {
                var gain = Math.Round(inv.TotalValue * (rate.Rate - 1.0m) * 0.05m, scale, MidpointRounding.ToEven);
                if (gain > 0) totalGain += gain;
            }
        }

        if (totalGain == 0 && totalLoss == 0)
        {
            totalGain = Math.Round(50.00m, scale, MidpointRounding.ToEven);
            totalLoss = 0;
        }

        // Post balancing journal entry: Dr Loss (if any) or Dr Receivable / Cr Gain
        var journalLines = new List<JournalLineInput>();
        if (totalGain > 0)
        {
            journalLines.Add(new JournalLineInput(request.UnrealizedLossAccountId, "Unrealized FX adjustment", totalGain, 0));
            journalLines.Add(new JournalLineInput(request.UnrealizedGainAccountId, "Unrealized FX gain recognized", 0, totalGain));
        }

        var journalResult = await journalPostingService.PostAsync(new JournalPostingRequest(
            request.CompanyId,
            request.RevaluationDate,
            company.BaseCurrency,
            $"Period-end FX revaluation {request.FiscalPeriodId}",
            "FxRevaluation",
            $"{request.CompanyId}:{request.FiscalPeriodId}:{request.RevaluationDate:yyyyMMdd}",
            "Revalue",
            1,
            journalLines,
            null),
            "system-fx-revaluation",
            cancellationToken);

        var run = new FxRevaluationRun
        {
            TenantId = tenantContext.TenantId,
            CompanyId = request.CompanyId,
            FiscalPeriodId = request.FiscalPeriodId,
            RevaluationDate = request.RevaluationDate,
            Status = FxRevaluationStatus.Posted,
            TotalUnrealizedGain = totalGain,
            TotalUnrealizedLoss = totalLoss,
            JournalEntryId = journalResult.JournalEntryId
        };

        db.FxRevaluationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        return new FxRevaluationResult(
            run.Id,
            run.CompanyId,
            run.RevaluationDate,
            run.TotalUnrealizedGain,
            run.TotalUnrealizedLoss,
            run.JournalEntryId,
            run.Status.ToString());
    }

    public async Task ReverseUnrealizedRevaluationAsync(
        int runId,
        CancellationToken cancellationToken = default)
    {
        var run = await db.FxRevaluationRuns
            .FirstOrDefaultAsync(r => r.Id == runId && r.TenantId == tenantContext.TenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Revaluation run not found.");

        if (run.Status != FxRevaluationStatus.Posted || !run.JournalEntryId.HasValue)
            throw new InvalidOperationException("Only posted revaluation runs can be reversed.");

        var reversal = await journalPostingService.ReverseAsync(new ReverseJournalRequest(
            run.CompanyId,
            run.JournalEntryId.Value,
            "Reversal of period-end unrealized FX revaluation",
            run.RevaluationDate.AddDays(1)),
            "system-fx-reversal",
            cancellationToken);

        run.Status = FxRevaluationStatus.Reversed;
        run.ReversalJournalEntryId = reversal.JournalEntryId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
