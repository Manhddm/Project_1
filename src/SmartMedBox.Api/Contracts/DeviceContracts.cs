namespace SmartMedBox.Api.Contracts;

// rtcTimeUtc/pendingEventCount nhận theo hợp đồng nhưng chưa dùng: firmware tự chỉnh RTC theo serverTimeUtc (D-26).
public sealed record HeartbeatRequest(string? FirmwareVersion, DateTimeOffset? RtcTimeUtc, int? PendingEventCount);

public sealed record HeartbeatResponse(
    string PairingState,
    DateTimeOffset ServerTimeUtc,
    int ScheduleVersion,
    int NextHeartbeatSeconds);

// batchId nhận theo hợp đồng nhưng không lưu: idempotency dựa trên (DeviceId, EventId).
public sealed record DeviceEventBatchRequest(string? BatchId, IReadOnlyList<DeviceEventItem>? Events);

public sealed record DeviceEventItem(string? EventId, int? CompartmentSlot, string? EventType, DateTimeOffset? OccurredAt);

// matchedDoseInstanceId/doseStatus chỉ có giá trị khi event xác nhận một dose (P4-03); ngược lại null.
public sealed record DeviceEventResultDto(
    string EventId,
    string Status,
    string? Code,
    Guid? MatchedDoseInstanceId,
    string? DoseStatus);

public sealed record DeviceEventBatchResponse(DateTimeOffset ServerTimeUtc, IReadOnlyList<DeviceEventResultDto> Results);

// API_CONTRACT §9.2: timeZoneId thuộc từng lịch, không có timezone cấp gói.
public sealed record ScheduleSyncItemDto(string MedicineName, decimal Quantity, string Unit, string Instructions);

public sealed record ScheduleSyncScheduleDto(
    Guid Id,
    int CompartmentSlot,
    string TimeOfDay,
    IReadOnlyList<int> DaysOfWeek,
    DateOnly StartDate,
    DateOnly? EndDate,
    int GracePeriodMinutes,
    string TimeZoneId,
    IReadOnlyList<ScheduleSyncItemDto> Items);

public sealed record ScheduleSyncResponse(
    int Version,
    DateTimeOffset ServerTimeUtc,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ScheduleSyncScheduleDto> Schedules);
