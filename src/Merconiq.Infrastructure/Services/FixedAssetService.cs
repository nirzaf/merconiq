using System.Globalization;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Core.Services;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Fixed-asset lifecycle over the finance kernel. Postings use the company's posting-role mapping, so
/// accounts are never hard-coded, and they are keyed by asset and action: a month of depreciation is
/// journal source "FixedAsset"/asset/"Depreciate"/month index, and a rerun replays it instead of charging
/// again. Each operation runs in one transaction with its status change, so a failure leaves nothing behind.
/// </summary>
public sealed class FixedAssetService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    IJournalPostingService journal,
    IAccountPostingPolicy accountPolicy,
    ILogger<FixedAssetService> logger) : IFixedAssetService
{
    /// <summary>Journal source document family used by every fixed-asset posting.</summary>
    public const string SourceDocumentType = "FixedAsset";

    private const string CapitalizeAction = "Capitalize";
    private const string DepreciateAction = "Depreciate";
    private const string DisposeAction = "Dispose";
    private const int MaximumCodeLength = 64;
    private const int MaximumNameLength = 200;

    /// <inheritdoc />
    public async Task<FixedAsset> RegisterAsync(
        RegisterFixedAssetRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(request.CompanyId);
        var code = request.AssetCode?.Trim() ?? string.Empty;
        if (code.Length is 0 or > MaximumCodeLength)
            throw new FinancePostingValidationException("An asset code of 1 to 64 characters is required.");
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaximumNameLength)
            throw new FinancePostingValidationException("An asset name of 1 to 200 characters is required.");
        if (request.DepreciationStartDate < request.AcquisitionDate)
            throw new FinancePostingValidationException("Depreciation cannot start before the acquisition date.");

        FixedAsset created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"fixed-assets:{request.CompanyId}", cancellationToken);

            var scale = await CurrencyScaleAsync(request.CompanyId, cancellationToken);
            // Validates the schedule against the company's scale before anything is stored.
            FixedAssetDepreciationSchedule.Build(
                request.CostAmount, request.SalvageValue, request.UsefulLifeMonths, request.DepreciationStartDate, scale);

            var duplicate = await context.FixedAssets.AnyAsync(
                asset => asset.CompanyId == request.CompanyId && asset.AssetCode == code, cancellationToken);
            if (duplicate)
                throw new FinancePostingConflictException($"Asset code '{code}' already exists in this company.");

            var asset = new FixedAsset
            {
                CompanyId = request.CompanyId,
                AssetCode = code,
                Name = name,
                AcquisitionDate = request.AcquisitionDate,
                DepreciationStartDate = request.DepreciationStartDate,
                CostAmount = request.CostAmount,
                SalvageValue = request.SalvageValue,
                UsefulLifeMonths = request.UsefulLifeMonths,
                Status = FixedAssetStatus.Registered
            };
            context.FixedAssets.Add(asset);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            created = asset;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Registered fixed asset {AssetCode} for company {CompanyId}", created.AssetCode, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FixedAsset>> ListAsync(
        int companyId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        return await context.FixedAssets
            .Where(asset => asset.CompanyId == companyId)
            .OrderBy(asset => asset.AssetCode)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FixedAssetPosition?> GetAsync(
        int companyId,
        int fixedAssetId,
        CancellationToken cancellationToken = default)
    {
        RequirePositiveCompany(companyId);
        var asset = await context.FixedAssets.SingleOrDefaultAsync(
            candidate => candidate.Id == fixedAssetId && candidate.CompanyId == companyId,
            cancellationToken);
        if (asset is null)
            return null;

        var postedPeriods = await CountDepreciationPostingsAsync(companyId, asset.Id, cancellationToken);
        var accumulated = 0m;
        if (asset.Status != FixedAssetStatus.Registered)
        {
            var scale = await CurrencyScaleAsync(companyId, cancellationToken);
            var schedule = BuildSchedule(asset, scale);
            accumulated = schedule.Take(postedPeriods).Sum(period => period.Amount);
        }

        return new FixedAssetPosition(asset, postedPeriods, accumulated, asset.CostAmount - accumulated);
    }

    /// <inheritdoc />
    public async Task<FixedAsset> CapitalizeAsync(
        int companyId,
        int fixedAssetId,
        int creditChartOfAccountId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        FixedAsset capitalized = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"fixed-assets:{companyId}", cancellationToken);

            var company = await RequireActiveCompanyAsync(companyId, cancellationToken);
            var asset = await FindAssetAsync(companyId, fixedAssetId, cancellationToken);
            if (asset.Status != FixedAssetStatus.Registered)
                throw new FinancePostingConflictException(
                    $"Asset '{asset.AssetCode}' is {asset.Status.ToString().ToLowerInvariant()} and cannot be capitalized.");

            var credit = await RequirePostingAccountAsync(companyId, creditChartOfAccountId, cancellationToken);
            var costAccount = await accountPolicy.ResolveAsync(companyId, AccountPostingRole.FixedAssets, cancellationToken);
            var result = await journal.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    asset.AcquisitionDate,
                    company.BaseCurrency,
                    $"Capitalize fixed asset {asset.AssetCode}: {asset.Name}",
                    SourceDocumentType,
                    asset.Id.ToString(CultureInfo.InvariantCulture),
                    CapitalizeAction,
                    1,
                    [
                        new JournalLineInput(costAccount.ChartOfAccountId, asset.Name, asset.CostAmount, 0m),
                        new JournalLineInput(credit.Id, asset.Name, 0m, asset.CostAmount)
                    ]),
                actor,
                cancellationToken);

            asset.Status = FixedAssetStatus.Capitalized;
            asset.CapitalizationJournalEntryId = result.JournalEntryId;
            asset.CapitalizedAtUtc = DateTime.UtcNow;
            asset.CapitalizedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            capitalized = asset;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Capitalized fixed asset {AssetCode} for company {CompanyId}", capitalized.AssetCode, companyId);
        return capitalized;
    }

    /// <inheritdoc />
    public async Task<FixedAssetDepreciationRun> RunDepreciationAsync(
        int companyId,
        DateOnly asOfDate,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);

        IReadOnlyList<FixedAssetDepreciationPosting> postings = [];
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"fixed-assets:{companyId}", cancellationToken);

            var company = await RequireActiveCompanyAsync(companyId, cancellationToken);
            var scale = company.CurrencyScale ?? 2;
            var assets = await context.FixedAssets
                .Where(asset => asset.CompanyId == companyId && asset.Status == FixedAssetStatus.Capitalized)
                .OrderBy(asset => asset.AssetCode)
                .ToListAsync(cancellationToken);

            var runPostings = new List<FixedAssetDepreciationPosting>();
            ResolvedPostingAccount? expense = null;
            ResolvedPostingAccount? accumulated = null;
            foreach (var asset in assets)
            {
                foreach (var period in BuildSchedule(asset, scale).Where(period => period.PeriodEnd <= asOfDate))
                {
                    expense ??= await accountPolicy.ResolveAsync(
                        companyId, AccountPostingRole.DepreciationExpense, cancellationToken);
                    accumulated ??= await accountPolicy.ResolveAsync(
                        companyId, AccountPostingRole.AccumulatedDepreciation, cancellationToken);

                    var result = await journal.PostAsync(
                        new JournalPostingRequest(
                            companyId,
                            period.PeriodEnd,
                            company.BaseCurrency,
                            $"Depreciation {asset.AssetCode} month {period.Index}",
                            SourceDocumentType,
                            asset.Id.ToString(CultureInfo.InvariantCulture),
                            DepreciateAction,
                            period.Index,
                            [
                                new JournalLineInput(expense.ChartOfAccountId, asset.Name, period.Amount, 0m),
                                new JournalLineInput(accumulated.ChartOfAccountId, asset.Name, 0m, period.Amount)
                            ]),
                        actor,
                        cancellationToken);
                    runPostings.Add(new FixedAssetDepreciationPosting(
                        asset.Id, period.Index, period.PeriodEnd, period.Amount, result.JournalEntryId, result.Replayed));
                }
            }

            postings = runPostings;
        }, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Depreciation run as of {AsOfDate} for company {CompanyId} produced {PostingCount} monthly postings",
            asOfDate, companyId, postings.Count);
        return new FixedAssetDepreciationRun(companyId, asOfDate, postings);
    }

    /// <inheritdoc />
    public async Task<FixedAsset> DisposeAsync(
        int companyId,
        int fixedAssetId,
        DateOnly disposalDate,
        decimal proceeds,
        int proceedsChartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        RequirePositiveCompany(companyId);
        var reasonText = RequireReason(reason);

        FixedAsset disposed = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"fixed-assets:{companyId}", cancellationToken);

            var company = await RequireActiveCompanyAsync(companyId, cancellationToken);
            var scale = company.CurrencyScale ?? 2;
            if (proceeds < 0m || proceeds != Math.Round(proceeds, scale, MidpointRounding.ToEven))
                throw new FinancePostingValidationException(
                    $"Disposal proceeds must be zero or more at {scale} decimal places.");

            var asset = await FindAssetAsync(companyId, fixedAssetId, cancellationToken);
            if (asset.Status != FixedAssetStatus.Capitalized)
                throw new FinancePostingConflictException(
                    $"Asset '{asset.AssetCode}' is {asset.Status.ToString().ToLowerInvariant()} and cannot be disposed.");
            if (disposalDate < asset.AcquisitionDate)
                throw new FinancePostingValidationException("The disposal date cannot be before the acquisition date.");

            var proceedsAccount = await RequirePostingAccountAsync(companyId, proceedsChartOfAccountId, cancellationToken);
            var due = BuildSchedule(asset, scale).Where(period => period.PeriodEnd <= disposalDate).ToList();
            var posted = await CountDepreciationPostingsAsync(companyId, asset.Id, cancellationToken);
            if (posted < due.Count)
                throw new FinancePostingConflictException(
                    $"Depreciation is posted for {posted} of {due.Count} months ending on or before {disposalDate:yyyy-MM-dd}; run depreciation through the disposal date first.");
            if (posted > due.Count)
                throw new FinancePostingConflictException(
                    $"Depreciation has been posted past the disposal date {disposalDate:yyyy-MM-dd}; dispose on a later date or correct the schedule.");

            var accumulated = due.Sum(period => period.Amount);
            var netBookValue = asset.CostAmount - accumulated;
            var gainOrLoss = proceeds - netBookValue;
            var gain = Math.Max(gainOrLoss, 0m);
            var loss = Math.Max(-gainOrLoss, 0m);

            var lines = new List<JournalLineInput>();
            if (proceeds > 0m)
                lines.Add(new JournalLineInput(proceedsAccount.Id, "Disposal proceeds", proceeds, 0m));
            if (accumulated > 0m)
            {
                var accumulatedAccount = await accountPolicy.ResolveAsync(
                    companyId, AccountPostingRole.AccumulatedDepreciation, cancellationToken);
                lines.Add(new JournalLineInput(accumulatedAccount.ChartOfAccountId, "Accumulated depreciation released", accumulated, 0m));
            }
            if (loss > 0m)
            {
                var lossAccount = await accountPolicy.ResolveAsync(companyId, AccountPostingRole.LossOnDisposal, cancellationToken);
                lines.Add(new JournalLineInput(lossAccount.ChartOfAccountId, "Loss on disposal", loss, 0m));
            }
            var costAccount = await accountPolicy.ResolveAsync(companyId, AccountPostingRole.FixedAssets, cancellationToken);
            lines.Add(new JournalLineInput(costAccount.ChartOfAccountId, "Cost removed", 0m, asset.CostAmount));
            if (gain > 0m)
            {
                var gainAccount = await accountPolicy.ResolveAsync(companyId, AccountPostingRole.GainOnDisposal, cancellationToken);
                lines.Add(new JournalLineInput(gainAccount.ChartOfAccountId, "Gain on disposal", 0m, gain));
            }

            var result = await journal.PostAsync(
                new JournalPostingRequest(
                    companyId,
                    disposalDate,
                    company.BaseCurrency,
                    $"Dispose fixed asset {asset.AssetCode}: {reasonText}",
                    SourceDocumentType,
                    asset.Id.ToString(CultureInfo.InvariantCulture),
                    DisposeAction,
                    1,
                    lines),
                actor,
                cancellationToken);

            asset.Status = FixedAssetStatus.Disposed;
            asset.DisposalDate = disposalDate;
            asset.DisposalProceeds = proceeds;
            asset.DisposalJournalEntryId = result.JournalEntryId;
            asset.DisposedAtUtc = DateTime.UtcNow;
            asset.DisposedBy = actor.Trim();
            asset.DisposalReason = reasonText;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            disposed = asset;
        }, cancellationToken: cancellationToken);

        logger.LogInformation("Disposed fixed asset {AssetCode} for company {CompanyId}", disposed.AssetCode, companyId);
        return disposed;
    }

    private static IReadOnlyList<FixedAssetDepreciationPeriod> BuildSchedule(FixedAsset asset, int scale) =>
        FixedAssetDepreciationSchedule.Build(
            asset.CostAmount, asset.SalvageValue, asset.UsefulLifeMonths, asset.DepreciationStartDate, scale);

    private async Task<int> CountDepreciationPostingsAsync(
        int companyId,
        int fixedAssetId,
        CancellationToken cancellationToken)
    {
        var assetKey = fixedAssetId.ToString(CultureInfo.InvariantCulture);
        return await context.JournalEntries.CountAsync(
            entry => entry.CompanyId == companyId &&
                     entry.SourceDocumentType == SourceDocumentType &&
                     entry.SourceDocumentId == assetKey &&
                     entry.SourceAction == DepreciateAction,
            cancellationToken);
    }

    private async Task<FixedAsset> FindAssetAsync(
        int companyId,
        int fixedAssetId,
        CancellationToken cancellationToken) =>
        await context.FixedAssets.SingleOrDefaultAsync(
            candidate => candidate.Id == fixedAssetId && candidate.CompanyId == companyId,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Fixed asset not found in this company.");

    private async Task<Company> RequireActiveCompanyAsync(int companyId, CancellationToken cancellationToken) =>
        await context.Companies.SingleOrDefaultAsync(
            candidate => candidate.Id == companyId && candidate.IsActive,
            cancellationToken)
        ?? throw new FinancePostingValidationException("Active company not found.");

    private async Task<int> CurrencyScaleAsync(int companyId, CancellationToken cancellationToken) =>
        (await RequireActiveCompanyAsync(companyId, cancellationToken)).CurrencyScale ?? 2;

    private async Task<ChartOfAccount> RequirePostingAccountAsync(
        int companyId,
        int chartOfAccountId,
        CancellationToken cancellationToken)
    {
        var account = await context.ChartOfAccounts.SingleOrDefaultAsync(
            candidate => candidate.Id == chartOfAccountId && candidate.CompanyId == companyId,
            cancellationToken)
            ?? throw new FinancePostingValidationException("Chart account not found in this company.");
        if (!account.IsActive)
            throw new FinancePostingValidationException($"Chart account '{account.AccountCode}' is inactive.");
        if (account.IsGroupAccount)
            throw new FinancePostingValidationException(
                $"Chart account '{account.AccountCode}' is a group account and cannot receive postings.");
        return account;
    }

    private static void RequirePositiveCompany(int companyId)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
    }

    private static string RequireReason(string reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500)
            throw new FinancePostingValidationException("A reason of 1 to 500 characters is required.");
        return trimmed;
    }
}
