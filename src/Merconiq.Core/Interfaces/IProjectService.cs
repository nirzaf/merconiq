using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// Projects, tasks and timesheets. Time approval freezes billable evidence; profitability reads
/// derive from approved lines only. Nothing here posts to the ledger.
/// </summary>
public interface IProjectService
{
    Task<Project> CreateProjectAsync(
        CreateProjectRequest request, string actor, CancellationToken cancellationToken = default);

    Task<ProjectTask> CreateTaskAsync(
        CreateProjectTaskRequest request, string actor, CancellationToken cancellationToken = default);

    Task<TimesheetEntry> LogTimeAsync(
        LogTimeRequest request, string workerId, CancellationToken cancellationToken = default);

    /// <summary>Worker submits a draft line for approval.</summary>
    Task<TimesheetEntry> SubmitTimeAsync(
        int companyId, int entryId, string workerId, CancellationToken cancellationToken = default);

    /// <summary>Approver freezes an approved line; approved lines are immutable evidence.</summary>
    Task<TimesheetEntry> ApproveTimeAsync(
        int companyId, int entryId, string actor, CancellationToken cancellationToken = default);

    Task<TimesheetEntry> RejectTimeAsync(
        int companyId, int entryId, string reason, string actor, CancellationToken cancellationToken = default);

    /// <summary>Approved-only profitability per project: hours and billable value.</summary>
    Task<ProjectProfitability> GetProfitabilityAsync(
        int companyId, int projectId, CancellationToken cancellationToken = default);
}
