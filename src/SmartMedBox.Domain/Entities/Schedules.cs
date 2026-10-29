using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Domain.Entities;

public sealed class MedicationSchedule
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid CompartmentId { get; set; }
    public TimeOnly TimeOfDay { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public int GracePeriodMinutes { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
}

public sealed class ScheduleDay
{
    public Guid ScheduleId { get; set; }
    public int DayOfWeek { get; set; }
}

public sealed class ScheduleItem
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
}

public sealed class DoseInstance
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid CompartmentId { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public int GracePeriodMinutes { get; set; }
    public DateTimeOffset DeadlineAt { get; set; }
    public DoseStatus Status { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed class DoseItemSnapshot
{
    public Guid Id { get; set; }
    public Guid DoseInstanceId { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
}
