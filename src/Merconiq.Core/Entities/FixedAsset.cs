namespace Merconiq.Core.Entities;

/// <summary>
/// One company fixed asset, straight-line depreciated over whole months. The register holds identity and
/// schedule parameters; every money effect is a posted journal from the finance kernel. Depreciation and
/// accumulated depreciation are derived from the schedule and those journals, never stored as counters.
/// </summary>
public sealed class FixedAsset : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-supplied asset number, unique within the company.</summary>
    public string AssetCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateOnly AcquisitionDate { get; set; }

    /// <summary>First calendar month that is depreciated; a full-month convention applies to that month.</summary>
    public DateOnly DepreciationStartDate { get; set; }

    /// <summary>Capitalized cost in base currency; always positive.</summary>
    public decimal CostAmount { get; set; }

    /// <summary>Residual value that is never depreciated; zero or less than the cost.</summary>
    public decimal SalvageValue { get; set; }

    public int UsefulLifeMonths { get; set; }

    public FixedAssetStatus Status { get; set; } = FixedAssetStatus.Registered;

    public int? CapitalizationJournalEntryId { get; set; }

    public DateTime? CapitalizedAtUtc { get; set; }

    public string? CapitalizedBy { get; set; }

    public DateOnly? DisposalDate { get; set; }

    public decimal? DisposalProceeds { get; set; }

    public int? DisposalJournalEntryId { get; set; }

    public DateTime? DisposedAtUtc { get; set; }

    public string? DisposedBy { get; set; }

    /// <summary>Mandatory reviewer reason recorded with the disposal.</summary>
    public string? DisposalReason { get; set; }
}
