namespace Merconiq.Core.Entities;

/// <summary>A customer master record owned by one tenant company.</summary>
public sealed class Customer : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>The legal or trading company that owns this customer record.</summary>
    public int CompanyId { get; set; }

    /// <summary>Company-local identifier, stored trimmed and uppercase.</summary>
    public string CustomerCode { get; set; } = string.Empty;

    /// <summary>Customer organization or trading name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional operational email address.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Optional operational phone number.</summary>
    public string? ContactPhone { get; set; }

    /// <summary>Optional billing postal address.</summary>
    public string? BillingAddress { get; set; }

    /// <summary>Optional shipping postal address.</summary>
    public string? ShippingAddress { get; set; }

    /// <summary>
    /// Optional configured payment-term duration. This is master-data configuration only;
    /// it does not calculate due dates or create document snapshots.
    /// </summary>
    public int? PaymentTermDays { get; set; }

    /// <summary>
    /// Maximum open receivable allowed for this customer in base currency. Null means no limit; a sales
    /// invoice that would exceed the limit needs a reviewer override with a recorded reason.
    /// </summary>
    public decimal? CreditLimit { get; set; }

    /// <summary>Reviewer's reason for the most recent credit-limit change.</summary>
    public string? CreditLimitChangeReason { get; set; }

    public string? CreditLimitChangedBy { get; set; }

    public DateTime? CreditLimitChangedAtUtc { get; set; }

    /// <summary>Whether this customer may be selected for future sales documents.</summary>
    public bool IsActive { get; set; } = true;

    public Company Company { get; set; } = null!;
}
