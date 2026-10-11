using Merconiq.Core.Entities;

namespace Merconiq.Core.Models;

/// <summary>Creates one employee.</summary>
public sealed record CreateEmployeeRequest(
    int CompanyId,
    string Code,
    string FullName,
    DateOnly HiredOn,
    string? Department = null,
    string? Position = null,
    string? UserId = null);

/// <summary>Requests one inclusive leave range.</summary>
public sealed record RequestLeaveRequest(
    int CompanyId,
    int EmployeeId,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason);

/// <summary>Approved leave days per type for one employee and year.</summary>
public sealed record LeaveBalance(
    int EmployeeId,
    int Year,
    decimal ApprovedAnnualDays,
    decimal ApprovedSickDays,
    decimal ApprovedUnpaidDays,
    int PendingRequests);


/// <summary>Employee self-service view of one's own record.</summary>
public sealed record EmployeeSelfView(
    int EmployeeId,
    string Code,
    string FullName,
    string? Department,
    bool IsActive);

