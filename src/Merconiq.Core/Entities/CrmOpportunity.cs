namespace Merconiq.Core.Entities;

/// <summary>
/// A sales opportunity in the pipeline. Winning it links the company's existing customer master record, after
/// which sales documents are raised against that customer through the existing sales flow.
/// </summary>
public sealed class CrmOpportunity : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The lead this opportunity came from, when it was converted from a lead.</summary>
    public int? LeadId { get; set; }

    /// <summary>The customer the opportunity was won against; set only when the opportunity is won.</summary>
    public int? CustomerId { get; set; }

    public CrmOpportunityStage Stage { get; set; } = CrmOpportunityStage.Prospecting;

    /// <summary>Expected value in base currency; a forecast, never a ledger amount.</summary>
    public decimal ExpectedValue { get; set; }

    public DateOnly ExpectedCloseDate { get; set; }

    public string Owner { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string CreatedByUser { get; set; } = string.Empty;

    public DateTime? ClosedAtUtc { get; set; }

    public string? ClosedBy { get; set; }

    public string? LostReason { get; set; }
}
