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
