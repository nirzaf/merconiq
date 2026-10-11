namespace Merconiq.Core.Entities;

/// <summary>Lifecycle of one quotation. Only open quotations can be converted to sales orders.</summary>
public enum QuotationStatus
{
    Open = 0,
    Accepted = 1,
    Expired = 2,
    Lost = 3
}
