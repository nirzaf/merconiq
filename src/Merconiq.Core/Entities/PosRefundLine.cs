namespace Merconiq.Core.Entities;

/// <summary>One refunded line against an original sale line; quantity bounded by sold-minus-refunded.</summary>
public sealed class PosRefundLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PosRefundId { get; set; }

    public int LineNumber { get; set; }

    public int PosSaleLineId { get; set; }

    public PosSaleLine PosSaleLine { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>The stock-return transaction created for this refund line, when restocked.</summary>
    public int? StockTransactionId { get; set; }

    public StockReturnDisposition Disposition { get; set; } = StockReturnDisposition.Restockable;

    /// <summary>Money returned for this line: quantity times the original sold unit price.</summary>
    public decimal RefundValue { get; set; }

    public PosRefund PosRefund { get; set; } = null!;
}
