namespace Merconiq.Core.Entities;

/// <summary>
/// Maps one company posting role to one company-owned chart account. Mappings are configuration:
/// they never post by themselves and never bypass the account validations applied at posting time.
/// </summary>
public sealed class CompanyAccountMapping : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public AccountPostingRole Role { get; set; }

    public int ChartOfAccountId { get; set; }

    public bool IsActive { get; set; } = true;

    public ChartOfAccount Account { get; set; } = null!;
}
