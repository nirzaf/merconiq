namespace Merconiq.Core.Entities;

/// <summary>One sold line. Price is snapshotted at checkout; the linked stock transaction carries the issued cost.</summary>
public sealed class PosSaleLine : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PosSaleId { get; set; }

    public int LineNumber { get; set; }

    public int ItemId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineValue { get; set; }

    /// <summary>The stock transaction that issued this quantity; refund evidence and COGS source.</summary>
    public int StockTransactionId { get; set; }

    public StockTransaction StockTransaction { get; set; } = null!;

    public PosSale PosSale { get; set; } = null!;
}
