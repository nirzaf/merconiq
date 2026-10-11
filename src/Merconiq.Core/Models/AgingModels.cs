namespace Merconiq.Core.Models;

/// <summary>One party's open balance inside one past-due bucket.</summary>
public sealed record AgingBucketRow(
    int PartyId,
    string PartyName,
    decimal NotDue,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal TotalOpen);

/// <summary>Open-invoice aging derived from posted documents minus posted allocations.</summary>
public sealed record AgingReport(
    int CompanyId,
    DateOnly AsOfDate,
    string Kind,
    IReadOnlyList<AgingBucketRow> Rows,
    decimal TotalOpen);
