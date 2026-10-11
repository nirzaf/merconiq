namespace Merconiq.Core.Entities;

public enum SerialNumberStatus
{
    InStock = 1,
    Delivered = 2,
    Scrapped = 3,
    Returned = 4
}

/// <summary>
/// Discrete per-unit serial number tracking with warranty and lifecycle history.
/// </summary>
public sealed class SerialNumber : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public string SerialNo { get; set; } = string.Empty;
    public SerialNumberStatus Status { get; set; } = SerialNumberStatus.InStock;
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public int? GoodsReceiptLineId { get; set; }
    public GoodsReceiptLine? GoodsReceiptLine { get; set; }
    public int? DeliveryNoteLineId { get; set; }
    public DeliveryNoteLine? DeliveryNoteLine { get; set; }
    public DateOnly? WarrantyExpiryDate { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? Notes { get; set; }
}
