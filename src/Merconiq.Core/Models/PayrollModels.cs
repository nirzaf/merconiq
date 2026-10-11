using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Sets (upserts) one employee's salary structure effective from a month.</summary>
public sealed record SetSalaryStructureRequest(
    int CompanyId,
    int EmployeeId,
    DateOnly EffectiveFrom,
    decimal BaseSalary,
    decimal Allowances);
