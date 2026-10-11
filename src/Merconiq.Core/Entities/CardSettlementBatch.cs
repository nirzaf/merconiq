namespace Merconiq.Core.Entities;

/// <summary>
/// One acquirer settlement batch as the processor reports it: gross card volume, the processor's fee, and
/// the net amount paid to the bank. Batches are imported evidence and are never edited.
/// </summary>
public sealed class CardSettlementBatch : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Processor's batch reference, unique within the company.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Date the processor settled the batch; card tenders must have been taken on or before it.</summary>
    public DateOnly SettlementDate { get; set; }

    public decimal GrossAmount { get; set; }

    public decimal FeeAmount { get; set; }

    /// <summary>Gross less fee, the amount the processor pays out.</summary>
    public decimal NetAmount { get; set; }

    public string ImportedBy { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; }
}
