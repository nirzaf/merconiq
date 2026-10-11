namespace Merconiq.Core.Entities;

/// <summary>One tender recorded for a checkout; the posting policy maps the method to its account.</summary>
public sealed class PosSaleTender : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int PosSaleId { get; set; }

    public int LineNumber { get; set; }

    /// <summary>Card is recorded as a bank-mapped clearing tender; settlement reconciliation is external.</summary>
    public PaymentMethod Method { get; set; }

    public decimal Amount { get; set; }

    public PosSale PosSale { get; set; } = null!;
}
