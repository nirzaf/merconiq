namespace Merconiq.Core.Entities;

/// <summary>One credited line against a posted delivery line; quantity bounded per line.</summary>
public sealed class SalesCreditNoteLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int SalesCreditNoteId { get; set; }

    public int LineNumber { get; set; }

    public int DeliveryNoteLineId { get; set; }

    public DeliveryNoteLine DeliveryNoteLine { get; set; } = null!;

    public int ItemId { get; set; }

    /// <summary>Returned quantity in base units.</summary>
    public int Quantity { get; set; }

    /// <summary>The stock-return transaction created for restocked returns.</summary>
    public int? StockTransactionId { get; set; }

    public StockReturnDisposition Disposition { get; set; } = StockReturnDisposition.Restockable;

    /// <summary>Credited revenue: quantity times the sold unit price.</summary>
    public decimal CreditValue { get; set; }

    public SalesCreditNote SalesCreditNote { get; set; } = null!;
}
