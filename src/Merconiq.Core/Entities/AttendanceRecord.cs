namespace Merconiq.Core.Entities;

/// <summary>
/// One workday attendance record per employee. Check-in and check-out are recorded by the
/// employee; the record is append-once per employee and date.
/// </summary>
public sealed class AttendanceRecord : AuditableEntity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public int EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    public DateOnly WorkDate { get; set; }

    /// <summary>Recorded check-in in UTC.</summary>
    public DateTime CheckInAtUtc { get; set; }

    /// <summary>Recorded check-out in UTC; null while the employee is still checked in.</summary>
    public DateTime? CheckOutAtUtc { get; set; }

    public string? Notes { get; set; }
}
