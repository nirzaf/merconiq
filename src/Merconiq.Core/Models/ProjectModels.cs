using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Creates one project.</summary>
public sealed record CreateProjectRequest(
    int CompanyId,
    string Code,
    string Name,
    int? CustomerId = null,
    string? Notes = null);

/// <summary>Creates one task inside a project.</summary>
public sealed record CreateProjectTaskRequest(int CompanyId, int ProjectId, string Name);

/// <summary>Logs one work line against a task.</summary>
public sealed record LogTimeRequest(
    int CompanyId,
    int ProjectTaskId,
    DateOnly WorkDate,
    decimal Hours,
    bool Billable,
    decimal HourlyRate,
    string? Notes = null);

/// <summary>Approved-only profitability for one project.</summary>
public sealed record ProjectProfitability(
    int ProjectId,
    string ProjectCode,
    decimal ApprovedHours,
    decimal BillableHours,
    decimal NonBillableHours,
    decimal BillableValue,
    int ApprovedEntries,
    int RejectedEntries);
