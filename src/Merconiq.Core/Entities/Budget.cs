namespace Merconiq.Core.Entities;

/// <summary>
/// One annual budget for one expense or income account. Budget-versus-actual reads compare
/// posted ledger lines against this plan; budgets never post.
/// </summary>
public sealed class Budget : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int ChartOfAccountId { get; set; }

    public ChartOfAccount Account { get; set; } = null!;

    /// <summary>Budget year.</summary>
    public int Year { get; set; }

    /// <summary>Planned annual amount in base currency.</summary>
    public decimal AnnualAmount { get; set; }

    public string? Notes { get; set; }
}
