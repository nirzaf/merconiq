using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Fixed-asset register, capitalization, straight-line depreciation and disposal. Every money effect is a
/// balanced journal through <see cref="IJournalPostingService"/>, and every operation runs as one atomic
/// unit with its status change, so a failed depreciation run or disposal leaves no partial ledger effect.
/// </summary>
public interface IFixedAssetService
{
    /// <summary>Registers one asset with a unique code; the schedule must be valid for the company's scale.</summary>
    Task<FixedAsset> RegisterAsync(
        RegisterFixedAssetRequest request,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the company's assets, by code.</summary>
    Task<IReadOnlyList<FixedAsset>> ListAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one asset with its posted depreciation, or <see langword="null"/> outside the company.</summary>
    Task<FixedAssetPosition?> GetAsync(
        int companyId,
        int fixedAssetId,
        CancellationToken cancellationToken = default);

    /// <summary>Posts the acquisition: Dr fixed assets at cost, Cr the chosen payment or payable account.</summary>
    Task<FixedAsset> CapitalizeAsync(
        int companyId,
        int fixedAssetId,
        int creditChartOfAccountId,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts every monthly depreciation whose month ends on or before the as-of date, for all capitalized
    /// assets. Committed months replay, so reruns never double-charge.
    /// </summary>
    Task<FixedAssetDepreciationRun> RunDepreciationAsync(
        int companyId,
        DateOnly asOfDate,
        string actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a capitalized asset. Every month that ends on or before the disposal date must already be
    /// depreciated; the gain or loss against net book value is posted with the proceeds.
    /// </summary>
    Task<FixedAsset> DisposeAsync(
        int companyId,
        int fixedAssetId,
        DateOnly disposalDate,
        decimal proceeds,
        int proceedsChartOfAccountId,
        string reason,
        string actor,
        CancellationToken cancellationToken = default);
}
