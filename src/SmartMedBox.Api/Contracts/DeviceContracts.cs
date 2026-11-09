namespace SmartMedBox.Api.Contracts;

// rtcTimeUtc/pendingEventCount nhận theo hợp đồng nhưng chưa dùng: chính sách lệch giờ là [TBD-D26].
public sealed record HeartbeatRequest(string? FirmwareVersion, DateTimeOffset? RtcTimeUtc, int? PendingEventCount);

public sealed record HeartbeatResponse(
    string PairingState,
    DateTimeOffset ServerTimeUtc,
    int ScheduleVersion,
    int NextHeartbeatSeconds);
