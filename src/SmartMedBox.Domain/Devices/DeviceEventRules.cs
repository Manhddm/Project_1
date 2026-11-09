using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Domain.Devices;

/// <summary>Ràng buộc sự kiện thiết bị theo API_CONTRACT §9.3 / §12 (P4-01).</summary>
public static class DeviceEventRules
{
    /// <summary>Khớp cột <c>DeviceEvents.EventId nvarchar(100)</c>.</summary>
    public const int MaxEventIdLength = 100;

    /// <summary>[ĐỀ XUẤT] trong API_CONTRACT: 1–100 sự kiện/batch.</summary>
    public const int MaxBatchSize = 100;

    public static bool IsValidEventId(string? eventId) =>
        !string.IsNullOrWhiteSpace(eventId) && eventId.Length <= MaxEventIdLength;

    /// <summary>Chỉ nhận đúng tên PascalCase của D-09; không nhận số hay sai hoa/thường như <c>Enum.TryParse</c>.</summary>
    public static bool TryParseEventType(string? value, out DeviceEventType eventType)
    {
        eventType = default;
        return value is not null
            && Enum.GetNames<DeviceEventType>().Contains(value, StringComparer.Ordinal)
            && Enum.TryParse(value, out eventType);
    }
}
