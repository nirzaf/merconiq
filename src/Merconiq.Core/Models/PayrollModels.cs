using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Sets (upserts) one employee's salary structure effective from a month.</summary>
public sealed record SetSalaryStructureRequest(
    int CompanyId,
    int EmployeeId,
    DateOnly EffectiveFrom,
    decimal BaseSalary,
    decimal Allowances);


/// <summary>Settlement reconciliation for one posted pay run: run totals versus posted journal.</summary>
public sealed record PayRunSettlementReport(
    int PayRunId,
    DateOnly Period,
    PayRunStatus Status,
    decimal DeclaredGross,
    decimal PostedGross,
    decimal DeclaredNet,
    decimal PostedNet,
    bool Reconciled,
    /// <summary>Payroll journals cannot be reversed generically; corrections use this path.</summary>
    bool Reversible);

