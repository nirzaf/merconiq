namespace Merconiq.Core.Entities;

/// <summary>
/// One posted payment against payables or receivables. Payments are immutable: a retry of the
/// same request key replays the committed payment. The amount must be fully allocated to open
/// documents in the same commit; advances and internal transfers are future increments.
/// </summary>
public sealed class PaymentEntry : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public PaymentEntryType PaymentType { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>Supplier paid (Pay) or customer charging (Receive); resolved per payment type.</summary>
    public int? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>Total payment amount; must equal the sum of the allocations.</summary>
    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public DateOnly PostingDate { get; set; }

    /// <summary>External payment reference (bank statement line, receipt slip).</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }

    /// <summary>Client idempotency key; one payment per tenant/company/key.</summary>
    public string RequestKey { get; set; } = string.Empty;

    /// <summary>Deterministic hash of the committed request payload.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public DateTime PostedAtUtc { get; set; }

    public string PostedBy { get; set; } = string.Empty;

    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}
