namespace Merconiq.Core.Entities;

/// <summary>
/// One service ticket with an SLA resolution clock derived from its priority (Critical 4h,
/// High 24h, Normal 72h, business-clock started at creation). Breaches are reported read-only;
/// SLA clocks never modify financial data.
/// </summary>
public sealed class ServiceTicket : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Company-scoped human-readable number allocated from the document sequences.</summary>
    public string TicketNumber { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    public TicketStatus Status { get; set; } = TicketStatus.Open;

    /// <summary>Optional project this ticket belongs to.</summary>
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ResolutionDueAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public string? ResolvedBy { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    public string? ClosedBy { get; set; }
}
