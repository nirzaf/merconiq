namespace Merconiq.Core.Entities;

public enum PriceListType
{
    Selling = 1,
    Buying = 2
}

/// <summary>
/// A named price list for selling or buying in a specific currency.
/// </summary>
public sealed class PriceList : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public PriceListType PriceListType { get; set; } = PriceListType.Selling;
    public bool IsActive { get; set; } = true;

    public ICollection<ItemPrice> ItemPrices { get; set; } = new List<ItemPrice>();
}

/// <summary>
/// Specific item rate within a price list with optional quantity tiers and date validity.
/// </summary>
public sealed class ItemPrice : AuditableEntity
{
    public int Id { get; set; }
    public int PriceListId { get; set; }
    public PriceList PriceList { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public decimal Rate { get; set; }
    public decimal MinQuantity { get; set; } = 1m;
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Promotional or tiered discount pricing rule applied on top of price lists.
/// </summary>
public sealed class PricingRule : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public PriceListType RuleType { get; set; } = PriceListType.Selling;
    public int? ItemId { get; set; }
    public Item? Item { get; set; }
    public int? PartyId { get; set; } // CustomerId for Selling, SupplierId for Buying
    public decimal MinQuantity { get; set; } = 0m;
    public decimal MinAmount { get; set; } = 0m;
    public decimal DiscountPercentage { get; set; } = 0m;
    public decimal DiscountAmount { get; set; } = 0m;
    public int Priority { get; set; } = 1; // Higher priority wins
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}
