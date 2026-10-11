using Merconiq.Core.Entities;
using Merconiq.Core.Models;

namespace Merconiq.Core.Interfaces;

/// <summary>
/// HR core: employees, attendance, and leave. Leave approval requires the Approve capability at
/// the API layer and is rejected against overlapping approved leave. Nothing here posts.
/// </summary>
public interface IHumanResourcesService
{
    Task<Employee> CreateEmployeeAsync(
        CreateEmployeeRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Records check-in for one employee and day; one open record per employee per day.</summary>
    Task<AttendanceRecord> CheckInAsync(
        int companyId, int employeeId, string? notes, string actor, CancellationToken cancellationToken = default);

    /// <summary>Closes the open attendance record for one employee and day.</summary>
    Task<AttendanceRecord> CheckOutAsync(
        int companyId, int employeeId, string actor, CancellationToken cancellationToken = default);

    Task<LeaveRequest> RequestLeaveAsync(
        RequestLeaveRequest request, string actor, CancellationToken cancellationToken = default);

    /// <summary>Approves one pending leave; overlapping approved leave is rejected first.</summary>
    Task<LeaveRequest> DecideLeaveAsync(
        int companyId, int leaveRequestId, bool approve, string? decisionReason, string actor,
        CancellationToken cancellationToken = default);

    /// <summary>Approved leave days per type for one employee in a year.</summary>
    Task<LeaveBalance> GetLeaveBalanceAsync(
        int companyId, int employeeId, int year, CancellationToken cancellationToken = default);
}
