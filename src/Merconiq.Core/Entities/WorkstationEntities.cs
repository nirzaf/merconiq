namespace Merconiq.Core.Entities;

public enum JobCardStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>
/// Physical production workstation with capacity and hourly labor/machine cost rates.
/// </summary>
public sealed class Workstation : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public decimal OperatingCapacityHours { get; set; } = 8m;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Defined routing operation step inside a production routing.
/// </summary>
public sealed class RoutingOperation : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public int WorkstationId { get; set; }
    public Workstation Workstation { get; set; } = null!;
    public int StandardDurationMinutes { get; set; } = 60;
    public decimal CostPerHour { get; set; }
}

/// <summary>
/// Shop-floor job card executed by operators for a specific work order routing step.
/// </summary>
public sealed class JobCard : AuditableEntity
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int WorkOrderId { get; set; }
    public WorkOrder WorkOrder { get; set; } = null!;
    public int RoutingOperationId { get; set; }
    public RoutingOperation RoutingOperation { get; set; } = null!;
    public string AssignedTechnician { get; set; } = string.Empty;
    public JobCardStatus Status { get; set; } = JobCardStatus.Pending;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int ActualDurationMinutes { get; set; }
    public decimal TotalCost { get; set; }
}
