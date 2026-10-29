using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Domain.Entities;

public sealed class Device
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string DeviceApiKeyHash { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public int ScheduleVersion { get; set; }
}

public sealed class DevicePairingCode
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
}

public sealed class Compartment
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public int SlotNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public sealed class DeviceEvent
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public Guid CompartmentId { get; set; }
    public DeviceEventType EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid? MatchedDoseInstanceId { get; set; }
}
