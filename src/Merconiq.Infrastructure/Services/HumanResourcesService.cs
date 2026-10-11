using Merconiq.Core.Entities;
using Merconiq.Core.Exceptions;
using Merconiq.Core.Interfaces;
using Merconiq.Core.Models;
using Merconiq.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Merconiq.Infrastructure.Services;

/// <summary>
/// HR core: employees, attendance, and leave. Writes run under a per-company advisory lock;
/// check-in is one open record per employee per day; leave approval (Approve capability at the
/// API) is rejected against overlapping approved leave. Nothing here posts to the ledger.
/// </summary>
public sealed class HumanResourcesService(
    InventoryDbContext context,
    IUnitOfWork unitOfWork,
    ILogger<HumanResourcesService> logger) : IHumanResourcesService
{
    /// <inheritdoc />
    public async Task<Employee> CreateEmployeeAsync(
        CreateEmployeeRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var code = RequireBounded(request.Code, "Employee code", 32);
        var name = RequireBounded(request.FullName, "Employee name", 200);
        Employee created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"hr:{request.CompanyId}", cancellationToken);
            if (!await context.Companies.AnyAsync(c => c.Id == request.CompanyId && c.IsActive, cancellationToken))
                throw new FinancePostingValidationException("Active company not found.");
            if (await context.Employees.AnyAsync(
                    employee => employee.CompanyId == request.CompanyId && employee.Code == code, cancellationToken))
                throw new FinancePostingConflictException(
                    $"An employee coded '{code}' already exists in this company.");

            created = new Employee
            {
                CompanyId = request.CompanyId,
                Code = code,
                FullName = name,
                Department = request.Department,
                Position = request.Position,
                HiredOn = request.HiredOn,
                IsActive = true,
                UserId = request.UserId
            };
            context.Employees.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        logger.LogInformation("Employee {Code} created for company {CompanyId}", created.Code, request.CompanyId);
        return created;
    }

    /// <inheritdoc />
    public async Task<AttendanceRecord> CheckInAsync(
        int companyId, int employeeId, string? notes, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        AttendanceRecord created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"hr:{companyId}", cancellationToken);
            await RequireEmployeeAsync(companyId, employeeId, cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (await context.AttendanceRecords.AnyAsync(
                    record => record.CompanyId == companyId && record.EmployeeId == employeeId &&
                        record.WorkDate == today && record.CheckOutAtUtc == null,
                    cancellationToken))
                throw new FinancePostingConflictException("The employee already has an open attendance record today.");

            created = new AttendanceRecord
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                WorkDate = today,
                CheckInAtUtc = DateTime.UtcNow,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
            };
            context.AttendanceRecords.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return created;
    }

    /// <inheritdoc />
    public async Task<AttendanceRecord> CheckOutAsync(
        int companyId, int employeeId, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        AttendanceRecord closed = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"hr:{companyId}", cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var open = await context.AttendanceRecords.SingleOrDefaultAsync(
                    record => record.CompanyId == companyId && record.EmployeeId == employeeId &&
                        record.WorkDate == today && record.CheckOutAtUtc == null,
                    cancellationToken)
                ?? throw new FinancePostingValidationException(
                    "No open attendance record exists for this employee today.");
            open.CheckOutAtUtc = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            closed = open;
        }, cancellationToken: cancellationToken);
        return closed;
    }

    /// <inheritdoc />
    public async Task<LeaveRequest> RequestLeaveAsync(
        RequestLeaveRequest request, string actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var reason = RequireBounded(request.Reason, "Leave reason", 500);
        if (request.EndDate < request.StartDate)
            throw new FinancePostingValidationException("The leave end date must not precede its start date.");

        LeaveRequest created = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"hr:{request.CompanyId}", cancellationToken);
            await RequireEmployeeAsync(request.CompanyId, request.EmployeeId, cancellationToken);

            created = new LeaveRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = request.EmployeeId,
                LeaveType = request.LeaveType,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Days = CountWorkingDays(request.StartDate, request.EndDate),
                Status = LeaveRequestStatus.Pending,
                Reason = reason
            };
            if (created.Days <= 0)
                throw new FinancePostingValidationException(
                    "The requested range contains no working days.");
            context.LeaveRequests.Add(created);
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
        }, cancellationToken: cancellationToken);
        return created;
    }

    /// <inheritdoc />
    public async Task<LeaveRequest> DecideLeaveAsync(
        int companyId, int leaveRequestId, bool approve, string? decisionReason, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        LeaveRequest decided = null!;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await unitOfWork.AcquireTenantOperationLockAsync($"hr:{companyId}", cancellationToken);
            var leave = await context.LeaveRequests.SingleOrDefaultAsync(
                    candidate => candidate.Id == leaveRequestId && candidate.CompanyId == companyId,
                    cancellationToken)
                ?? throw new FinancePostingValidationException("Leave request not found in this company.");
            if (leave.Status != LeaveRequestStatus.Pending)
                throw new FinancePostingConflictException(
                    $"The leave request was already {leave.Status.ToString().ToLowerInvariant()}.");

            if (approve)
            {
                // Overlapping approved leave for the same employee is rejected before commit.
                var overlaps = await context.LeaveRequests.AnyAsync(
                    candidate => candidate.CompanyId == companyId &&
                        candidate.EmployeeId == leave.EmployeeId &&
                        candidate.Status == LeaveRequestStatus.Approved &&
                        candidate.StartDate <= leave.EndDate &&
                        candidate.EndDate >= leave.StartDate,
                    cancellationToken);
                if (overlaps)
                    throw new FinancePostingConflictException(
                        "Approved leave already overlaps this range for the employee.");
            }
            else if (string.IsNullOrWhiteSpace(decisionReason))
            {
                throw new FinancePostingValidationException(
                    "A rejection requires a recorded decision reason.");
            }

            leave.Status = approve ? LeaveRequestStatus.Approved : LeaveRequestStatus.Rejected;
            leave.DecidedAtUtc = DateTime.UtcNow;
            leave.DecidedBy = actor.Trim();
            leave.DecisionReason = string.IsNullOrWhiteSpace(decisionReason) ? null : decisionReason.Trim();
            await unitOfWork.SaveChangesAsAsync(actor, cancellationToken);
            decided = leave;
        }, cancellationToken: cancellationToken);
        logger.LogInformation(
            "Leave request {LeaveRequestId} {Decision} for company {CompanyId}",
            leaveRequestId, approve ? "approved" : "rejected", companyId);
        return decided;
    }

    /// <inheritdoc />
    public async Task<LeaveBalance> GetLeaveBalanceAsync(
        int companyId, int employeeId, int year, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0)
            throw new FinancePostingValidationException("A positive CompanyId is required.");
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var approved = await context.LeaveRequests
            .Where(leave => leave.CompanyId == companyId && leave.EmployeeId == employeeId &&
                leave.Status == LeaveRequestStatus.Approved &&
                leave.StartDate <= yearEnd && leave.EndDate >= yearStart)
            .ToListAsync(cancellationToken);
        var pending = await context.LeaveRequests
            .CountAsync(leave => leave.CompanyId == companyId && leave.EmployeeId == employeeId &&
                leave.Status == LeaveRequestStatus.Pending, cancellationToken);

        decimal Sum(LeaveType type) => approved
            .Where(leave => leave.LeaveType == type)
            .Sum(leave => leave.Days);
        return new LeaveBalance(employeeId, year, Sum(LeaveType.Annual), Sum(LeaveType.Sick),
            Sum(LeaveType.Unpaid), pending);
    }

    private async Task RequireEmployeeAsync(
        int companyId, int employeeId, CancellationToken cancellationToken)
    {
        if (!await context.Employees.AnyAsync(
                employee => employee.Id == employeeId && employee.CompanyId == companyId && employee.IsActive,
                cancellationToken))
            throw new FinancePostingValidationException("Active employee not found in this company.");
    }

    private static decimal CountWorkingDays(DateOnly start, DateOnly end)
    {
        var days = 0m;
        for (var cursor = start; cursor <= end; cursor = cursor.AddDays(1))
        {
            if (cursor.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days++;
            }
        }
        return days;
    }

    private static string RequireBounded(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLength)
            throw new FinancePostingValidationException($"{field} of 1 to {maxLength} characters is required.");
        return trimmed;
    }
}
