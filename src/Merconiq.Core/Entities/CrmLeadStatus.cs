namespace Merconiq.Core.Entities;

/// <summary>Lead qualification. A converted lead has become an opportunity; a disqualified lead stays on record.</summary>
public enum CrmLeadStatus
{
    New = 0,
    Qualified = 1,
    Converted = 2,
    Disqualified = 3
}
