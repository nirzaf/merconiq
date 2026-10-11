using System.Globalization;
using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// Projects, tasks and timesheets. Writes run under a per-company advisory lock; approval
/// freezes a line as immutable billable evidence; rejected lines are terminal. Profitability
/// reads derive from approved lines only. Nothing here posts to the ledger.
/// </summary>
public sealed class ProjectService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<ProjectService> logger) : IProjectService
{
    private const decimal MaximumHoursPerEntry = 24m;

    /// <inheritdoc />
    public async Task<Project> CreateProjectAsync(
        CreateProjectRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var code = RequireBounded(request.Code, "Project code", 32);
        var name = RequireBounded(request.Name, "Project name", 200);
        Project created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"project:{request.CompanyId}", cancellationToken);
            if (!await context.Companies.AnyAsync(c => c.Id == request.CompanyId && c.IsActive, cancellationToken))
                throw new FinancePostingValidationException("Active company not found.");
            if (await context.Projects.AnyAsync(
                    project => project.CompanyId == request.CompanyId && project.Code == code, cancellationToken))
                throw new FinancePostingConflictException(
                    $"A project coded '{code}' already exists in this company.");
            if (request.CustomerId is { } customerId &&
                !await context.Customers.AnyAsync(
                    customer => customer.Id == customerId && customer.CompanyId == request.CompanyId,
                    cancellationToken))
                throw new FinancePostingValidationException("Customer not found in this company.");

            created = new Project
            {
                CompanyId = request.CompanyId,
                Code = code,
                Name = name,
                CustomerId = request.CustomerId,
                Status = ProjectStatus.Active,
                Notes = request.Notes
            };
            context.Projects.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        logger.LogInformation("Project {Code} created for company {CompanyId}", created.Code, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<ProjectTask> CreateTaskAsync(
        CreateProjectTaskRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var name = RequireBounded(request.Name, "Task name", 200);
        ProjectTask created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"project:{request.CompanyId}", cancellationToken);
            var project = await context.Projects.SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ProjectId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Project not found in this company.");
            if (project.Status != ProjectStatus.Active)
                throw new FinancePostingConflictException("Tasks can only be added to active projects.");

            created = new ProjectTask
            {
                CompanyId = request.CompanyId, ProjectId = project.Id, Name = name
            };
            context.ProjectTasks.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return created;
    }

    /// <inheritdoc />
    public async Task<TimesheetEntry> LogTimeAsync(
        LogTimeRequest request, string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (request.Hours <= 0 || request.Hours > MaximumHoursPerEntry ||
            request.Hours != decimal.Round(request.Hours, 2, MidpointRounding.AwayFromZero))
            throw new FinancePostingValidationException(
                $"Hours must be between 0.01 and {MaximumHoursPerEntry.ToString(CultureInfo.InvariantCulture)} with at most 2 decimals.");
        if (request.HourlyRate < 0)
            throw new FinancePostingValidationException("The hourly rate must not be negative.");

        TimesheetEntry created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"timesheet:{request.CompanyId}", cancellationToken);
            var task = await context.ProjectTasks
                .Include(candidate => candidate.Project)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ProjectTaskId && candidate.CompanyId == request.CompanyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Project task not found in this company.");
            if (task.Project.Status != ProjectStatus.Active)
                throw new FinancePostingConflictException("Time cannot be logged against a non-active project.");

            created = new TimesheetEntry
            {
                CompanyId = request.CompanyId,
                ProjectId = task.ProjectId,
                ProjectTaskId = task.Id,
                WorkerId = workerId.Trim(),
                WorkDate = request.WorkDate,
                Hours = request.Hours,
                Billable = request.Billable,
                HourlyRate = decimal.Round(request.HourlyRate, 6, MidpointRounding.AwayFromZero),
                Status = TimesheetEntryStatus.Draft,
                Notes = request.Notes
            };
            context.TimesheetEntries.Add(created);
            await unitOfWork.SaveChangesAsAsync(workerId.Trim(), cancellationToken);
        }, cancellationToken: cancellationToken);
        return created;
    }

    /// <inheritdoc />
    public async Task<TimesheetEntry> SubmitTimeAsync(
        int companyId, int entryId, string workerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        TimesheetEntry submitted = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"timesheet:{companyId}", cancellationToken);
            var entry = await FindEntryAsync(companyId, entryId, cancellationToken);
            if (entry.WorkerId != workerId.Trim())
                throw new UnauthorizedAccessException("Only the line's owner can submit it.");
            if (entry.Status != TimesheetEntryStatus.Draft)
                throw new FinancePostingConflictException($"A {entry.Status} line cannot be submitted.");
            entry.Status = TimesheetEntryStatus.Submitted;
            await unitOfWork.SaveChangesAsAsync(workerId.Trim(), cancellationToken);
            submitted = entry;
        }, cancellationToken: cancellationToken);
        return submitted;
    }

    /// <inheritdoc />
    public async Task<TimesheetEntry> ApproveTimeAsync(
        int companyId, int entryId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        TimesheetEntry approved = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"timesheet:{companyId}", cancellationToken);
            var entry = await FindEntryAsync(companyId, entryId, cancellationToken);
            if (entry.Status != TimesheetEntryStatus.Submitted)
                throw new FinancePostingConflictException(
                    $"A {entry.Status} line cannot be approved; only submitted lines reach approval.");
            entry.Status = TimesheetEntryStatus.Approved;
            entry.ApprovedAtUtc = DateTime.UtcNow;
            entry.ApprovedBy = actor.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            approved = entry;
        }, cancellationToken: cancellationToken);
        logger.LogInformation("Timesheet entry {EntryId} approved for company {CompanyId}", entryId, companyId);
        return approved;
    }

    /// <inheritdoc />
    public async Task<TimesheetEntry> RejectTimeAsync(
        int companyId, int entryId, string reason, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var trimmedReason = RequireBounded(reason, "Rejection reason", 500);
        TimesheetEntry rejected = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"timesheet:{companyId}", cancellationToken);
            var entry = await FindEntryAsync(companyId, entryId, cancellationToken);
            if (entry.Status is not (TimesheetEntryStatus.Draft or TimesheetEntryStatus.Submitted))
                throw new FinancePostingConflictException(
                    $"A {entry.Status} line cannot be rejected.");
            entry.Status = TimesheetEntryStatus.Rejected;
            entry.RejectionReason = trimmedReason;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            rejected = entry;
        }, cancellationToken: cancellationToken);
        return rejected;
    }

    /// <inheritdoc />
    public async Task<ProjectProfitability> GetProfitabilityAsync(
        int companyId, int projectId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var project = await context.Projects.SingleOrDefaultAsync(
                candidate => candidate.Id == projectId && candidate.CompanyId == companyId, cancellationToken)
            ?? throw new FinancePostingValidationException("Project not found in this company.");

        var approved = await context.TimesheetEntries
            .Where(entry => entry.ProjectId == projectId && entry.Status == TimesheetEntryStatus.Approved)
            .ToListAsync(cancellationToken);
        var rejectedCount = await context.TimesheetEntries
            .CountAsync(entry => entry.ProjectId == projectId && entry.Status == TimesheetEntryStatus.Rejected,
                cancellationToken);

        var billable = approved.Where(entry => entry.Billable).ToList();
        return new ProjectProfitability(
            project.Id,
            project.Code,
            approved.Sum(entry => entry.Hours),
            billable.Sum(entry => entry.Hours),
            approved.Where(entry => !entry.Billable).Sum(entry => entry.Hours),
            billable.Sum(entry => decimal.Round(entry.Hours * entry.HourlyRate, 2, MidpointRounding.AwayFromZero)),
            approved.Count,
            rejectedCount);
    }

    private Task<TimesheetEntry> FindEntryAsync(
        int companyId, int entryId, CancellationToken cancellationToken) =>
        context.TimesheetEntries.SingleOrDefaultAsync(
            entry => entry.Id == entryId && entry.CompanyId == companyId, cancellationToken)
        ?? throw new FinancePostingValidationException("Timesheet entry not found in this company.");

    private static string RequireBounded(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new FinancePostingValidationException($"{field} of 1 to {maxLength} characters is required.");
        return trimmed;
    }
}
