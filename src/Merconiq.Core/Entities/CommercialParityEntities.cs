namespace Merconiq.Core.Entities;

public enum SubcontractingStatus
{
    Draft = 1,
    Dispatched = 2,
    Received = 3,
    Cancelled = 4
}

public enum DunningLevel
{
    FirstReminder = 1,
    SecondWarning = 2,
    FinalDemand = 3
}

/// <summary>
/// Subcontracting production order delegating finishing work to an external vendor
/// while supplying raw materials and tracking consumption.
/// </summary>
public sealed class SubcontractingOrder : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public int FinishedItemId { get; set; }
    public Item FinishedItem { get; set; } = null!;
    public int QuantityToProduce { get; set; }
    public SubcontractingStatus Status { get; set; } = SubcontractingStatus.Draft;
    public decimal ServiceRate { get; set; }
    public decimal TotalServiceCost { get; set; }
    public int? WorkOrderId { get; set; }

    public ICollection<SubcontractingSuppliedItem> SuppliedItems { get; set; } = new List<SubcontractingSuppliedItem>();
}

public sealed class SubcontractingSuppliedItem : AuditableEntity
{
    public int Id { get; set; }
    public int SubcontractingOrderId { get; set; }
    public SubcontractingOrder SubcontractingOrder { get; set; } = null!;
    public int ComponentItemId { get; set; }
    public Item ComponentItem { get; set; } = null!;
    public decimal RequiredQuantity { get; set; }
    public decimal TransferredQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
}

/// <summary>
/// Sales partner commission accrual on sales documents.
/// </summary>
public sealed class SalesPartnerCommission : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public decimal CommissionPercentage { get; set; }
    public int SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    public decimal InvoicedAmount { get; set; }
    public decimal CommissionAmount { get; set; }
    public bool IsPaid { get; set; }
}

/// <summary>
/// Overdue accounts receivable dunning reminder notice with late fee calculation.
/// </summary>
public sealed class DunningNotice : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int SalesInvoiceId { get; set; }
    public SalesInvoice SalesInvoice { get; set; } = null!;
    public DunningLevel Level { get; set; } = DunningLevel.FirstReminder;
    public decimal OutstandingAmount { get; set; }
    public decimal DunningFee { get; set; }
    public decimal InterestAmount { get; set; }
    public DateOnly NoticeDate { get; set; }
    public DateOnly DueDate { get; set; }
}
