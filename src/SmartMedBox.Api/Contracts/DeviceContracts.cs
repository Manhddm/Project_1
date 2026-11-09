namespace SmartMedBox.Api.Contracts;

// rtcTimeUtc/pendingEventCount nhận theo hợp đồng nhưng chưa dùng: chính sách lệch giờ là [TBD-D26].
public sealed record HeartbeatRequest(string? FirmwareVersion, DateTimeOffset? RtcTimeUtc, int? PendingEventCount);

public sealed record HeartbeatResponse(
    string PairingState,
    DateTimeOffset ServerTimeUtc,
    int ScheduleVersion,
    int NextHeartbeatSeconds);

// batchId nhận theo hợp đồng nhưng không lưu: idempotency dựa trên (DeviceId, EventId).
public sealed record DeviceEventBatchRequest(string? BatchId, IReadOnlyList<DeviceEventItem>? Events);

public sealed record DeviceEventItem(string? EventId, int? CompartmentSlot, string? EventType, DateTimeOffset? OccurredAt);

// matchedDoseInstanceId/doseStatus luôn null cho tới P4-03 (ghép dose).
public sealed record DeviceEventResultDto(
    string EventId,
    string Status,
    string? Code,
    Guid? MatchedDoseInstanceId,
    string? DoseStatus);

public sealed record DeviceEventBatchResponse(DateTimeOffset ServerTimeUtc, IReadOnlyList<DeviceEventResultDto> Results);
