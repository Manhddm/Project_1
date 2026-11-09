using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

public enum DeviceEventResultStatus
{
    Accepted,
    Duplicate,
    Rejected,
}

public enum DeviceEventRejection
{
    CompartmentInvalid,
    EventTypeInvalid,
    EventTimeInvalid,
}

public sealed record DeviceEventInput(string EventId, int? CompartmentSlot, string? EventType, DateTimeOffset? OccurredAt);

public sealed record DeviceEventResult(
    string EventId,
    DeviceEventResultStatus Status,
    DeviceEventRejection? Rejection = null,
    Guid? MatchedDoseInstanceId = null,
    DoseStatus? DoseStatus = null);

/// <summary>
/// Nhận batch sự kiện thiết bị (P4-02, API_CONTRACT §9.3). Append-only, idempotent theo <c>UNIQUE (DeviceId, EventId)</c>;
/// mỗi event hợp lệ được ghi độc lập nên phần tử lỗi không rollback phần tử khác. Không ghép dose (P4-03).
/// </summary>
public sealed class DeviceEventIngestionService(AppDbContext db, IClock clock, DeviceEventMatchingService matching)
{
    /// <summary>Trả null nếu thiết bị chưa có chủ. Kết quả theo đúng thứ tự mảng đầu vào, một phần tử cho mỗi event.</summary>
    public async Task<IReadOnlyList<DeviceEventResult>?> IngestAsync(
        Guid deviceKey, IReadOnlyList<DeviceEventInput> events, CancellationToken cancellationToken = default)
    {
        var paired = await db.Devices.AnyAsync(x => x.Id == deviceKey && x.OwnerUserId != null, cancellationToken);
        if (!paired)
        {
            return null;
        }

        var compartments = await db.Compartments.AsNoTracking()
            .Where(x => x.DeviceId == deviceKey)
            .ToDictionaryAsync(x => x.SlotNumber, x => x.Id, cancellationToken);
        var ids = events.Select(x => x.EventId).Distinct().ToList();
        var existing = (await db.DeviceEvents.AsNoTracking()
                .Where(x => x.DeviceId == deviceKey && ids.Contains(x.EventId))
                .Select(x => x.EventId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var results = new DeviceEventResult?[events.Count];
        var now = clock.UtcNow;

        // Xử lý theo occurredAt rồi eventId (không theo thứ tự mảng) để batch xáo cho cùng kết quả.
        // [TBD-D26] Chưa chốt ngưỡng tương lai/quá cũ nên chưa từ chối theo khoảng thời gian; occurredAt giữ nguyên.
        var ordered = events
            .Select((input, index) => (input, index))
            .OrderBy(x => x.input.OccurredAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.input.EventId, StringComparer.Ordinal)
            .ThenBy(x => x.index);
        foreach (var (input, index) in ordered)
        {
            var rejection = Validate(input, compartments, out var eventType, out var compartmentId);
            if (rejection is not null)
            {
                results[index] = new DeviceEventResult(input.EventId, DeviceEventResultStatus.Rejected, rejection);
                continue;
            }

            if (!existing.Add(input.EventId))
            {
                var duplicate = await db.DeviceEvents.AsNoTracking()
                    .Where(x => x.DeviceId == deviceKey && x.EventId == input.EventId)
                    .Select(x => new { x.MatchedDoseInstanceId })
                    .SingleAsync(cancellationToken);
                var duplicateStatus = duplicate.MatchedDoseInstanceId is { } duplicateDoseId
                    ? await db.DoseInstances.AsNoTracking()
                        .Where(x => x.Id == duplicateDoseId)
                        .Select(x => (DoseStatus?)x.Status)
                        .SingleOrDefaultAsync(cancellationToken)
                    : null;
                results[index] = new DeviceEventResult(
                    input.EventId,
                    DeviceEventResultStatus.Duplicate,
                    MatchedDoseInstanceId: duplicate.MatchedDoseInstanceId,
                    DoseStatus: duplicateStatus);
                continue;
            }

            var deviceEvent = new DeviceEvent
            {
                DeviceId = deviceKey,
                EventId = input.EventId,
                CompartmentId = compartmentId,
                EventType = eventType,
                OccurredAt = input.OccurredAt!.Value,
                ReceivedAt = now,
            };
            db.DeviceEvents.Add(deviceEvent);
            var status = DeviceEventResultStatus.Accepted;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // Batch song song đã ghi cùng (DeviceId, EventId) giữa lúc kiểm tra và lúc ghi.
                status = DeviceEventResultStatus.Duplicate;
            }
            if (status == DeviceEventResultStatus.Accepted)
            {
                var match = await matching.MatchAsync(deviceKey, input.EventId, cancellationToken);
                results[index] = new DeviceEventResult(
                    input.EventId,
                    status,
                    MatchedDoseInstanceId: match.DoseInstanceId,
                    DoseStatus: match.DoseStatus);
            }

            db.Entry(deviceEvent).State = EntityState.Detached;
            results[index] ??= new DeviceEventResult(input.EventId, status);
        }

        return results!;
    }

    private static DeviceEventRejection? Validate(
        DeviceEventInput input,
        IReadOnlyDictionary<int, Guid> compartments,
        out DeviceEventType eventType,
        out Guid compartmentId)
    {
        compartmentId = default;
        if (!DeviceEventRules.TryParseEventType(input.EventType, out eventType))
        {
            return DeviceEventRejection.EventTypeInvalid;
        }

        if (input.OccurredAt is null)
        {
            return DeviceEventRejection.EventTimeInvalid;
        }

        return input.CompartmentSlot is { } slot && compartments.TryGetValue(slot, out compartmentId)
            ? null
            : DeviceEventRejection.CompartmentInvalid;
    }
}
