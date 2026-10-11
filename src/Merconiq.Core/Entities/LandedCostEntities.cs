namespace Merconiq.Core.Entities;

public enum LandedCostStatus
{
    Draft = 1,
    Submitted = 2,
    Cancelled = 3
}

/// <summary>
/// Landed cost voucher apportioning additional import/freight/customs charges
/// across received inventory from one or more goods receipts.
/// </summary>
public sealed class LandedCostVoucher : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public LandedCostStatus Status { get; set; } = LandedCostStatus.Draft;
    public decimal TotalAdditionalCharges { get; set; }
    public int? JournalEntryId { get; set; }
    public string? JournalNumber { get; set; }
    public string? Notes { get; set; }

    public ICollection<LandedCostReceipt> Receipts { get; set; } = new List<LandedCostReceipt>();
    public ICollection<LandedCostCharge> Charges { get; set; } = new List<LandedCostCharge>();
    public ICollection<LandedCostItemAllocation> ItemAllocations { get; set; } = new List<LandedCostItemAllocation>();
}

public sealed class LandedCostReceipt : AuditableEntity
{
    public int Id { get; set; }
    public int LandedCostVoucherId { get; set; }
    public LandedCostVoucher LandedCostVoucher { get; set; } = null!;
    public int GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
}

public sealed class LandedCostCharge : AuditableEntity
{
    public int Id { get; set; }
    public int LandedCostVoucherId { get; set; }
    public LandedCostVoucher LandedCostVoucher { get; set; } = null!;
    public int ExpenseAccountId { get; set; }
    public ChartOfAccount ExpenseAccount { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class LandedCostItemAllocation : AuditableEntity
{
    public int Id { get; set; }
    public int LandedCostVoucherId { get; set; }
    public LandedCostVoucher LandedCostVoucher { get; set; } = null!;
    public int GoodsReceiptLineId { get; set; }
    public GoodsReceiptLine GoodsReceiptLine { get; set; } = null!;
    public int ItemId { get; set; }
    public Item Item { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
    public decimal ApplicableCharges { get; set; }
}
