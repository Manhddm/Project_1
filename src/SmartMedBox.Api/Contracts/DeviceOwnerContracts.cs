using System.ComponentModel.DataAnnotations;

namespace SmartMedBox.Api.Contracts;

public sealed class PairDeviceRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public sealed record CompartmentDto(int SlotNumber, string Name, bool IsEnabled);

public sealed record PairedDeviceDto(
    Guid Id,
    string SerialNumber,
    DateTimeOffset PairedAt,
    int ScheduleVersion,
    IReadOnlyList<CompartmentDto> Compartments);

public sealed record PairDeviceResponse(PairedDeviceDto Device);

// isOnline luôn null cho tới khi chốt ngưỡng online/offline [TBD-D12]; client dựa vào lastSeenAt.
public sealed record MyDeviceDto(
    Guid Id,
    string SerialNumber,
    int ScheduleVersion,
    DateTimeOffset? LastSeenAt,
    bool? IsOnline,
    IReadOnlyList<CompartmentDto> Compartments);
