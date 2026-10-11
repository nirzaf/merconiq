using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Registers one fixed asset and its straight-line schedule parameters for a company.</summary>
public sealed record RegisterFixedAssetRequest(
    int CompanyId,
    string AssetCode,
    string Name,
    DateOnly AcquisitionDate,
    DateOnly DepreciationStartDate,
    decimal CostAmount,
    decimal SalvageValue,
    int UsefulLifeMonths);

/// <summary>An asset with its posted depreciation and the resulting carrying amount.</summary>
public sealed record FixedAssetPosition(
    FixedAsset Asset,
    int PostedDepreciationPeriods,
    decimal AccumulatedDepreciation,
    decimal NetBookValue);

/// <summary>One monthly depreciation posting produced by a run, or the replayed committed posting.</summary>
public sealed record FixedAssetDepreciationPosting(
    int FixedAssetId,
    int PeriodIndex,
    DateOnly PeriodEnd,
    decimal Amount,
    int JournalEntryId,
    bool Replayed);

/// <summary>The outcome of one depreciation run as of a date.</summary>
public sealed record FixedAssetDepreciationRun(
    int CompanyId,
    DateOnly AsOfDate,
    IReadOnlyList<FixedAssetDepreciationPosting> Postings);
