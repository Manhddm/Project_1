using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Schedules;

namespace SmartMedBox.Infrastructure.Devices;

public sealed record DeviceEventMatch(Guid? DoseInstanceId, DoseStatus? DoseStatus);

public sealed class DeviceEventMatchingService(AppDbContext db, IClock clock)
{
    private const int MaxAttempts = 10;

    public async Task<DeviceEventMatch> MatchAsync(
        Guid deviceId,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await MatchOnceAsync(deviceId, eventId, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsDeadlock(ex))
            {
                // Hai batch cùng ghép một dose (Serializable): nạn nhân deadlock bỏ thay đổi đang track rồi đọc lại bản đã commit.
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(10, 50) * attempt, cancellationToken);
            }
        }
    }

    private async Task<DeviceEventMatch> MatchOnceAsync(Guid deviceId, string eventId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var deviceEvent = await db.DeviceEvents
            .SingleAsync(x => x.DeviceId == deviceId && x.EventId == eventId, cancellationToken);
        if (deviceEvent.EventType != DeviceEventType.LidOpened)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeviceEventMatch(null, null);
        }

        // Chỉ chọn dose mà event này còn đổi được: chưa xác nhận (ưu tiên, theo ScheduledAt),
        // hoặc đã xác nhận ở occurredAt muộn hơn (D-24d). Dose đã xác nhận sớm hơn không chặn dose khác cùng ngăn.
        var occurredAt = deviceEvent.OccurredAt;
        var dose = await db.DoseInstances
            .Where(x => x.DeviceId == deviceId
                && x.CompartmentId == deviceEvent.CompartmentId
                && x.ScheduledAt <= occurredAt
                && x.DeadlineAt >= occurredAt
                && (x.Status != DoseStatus.Confirmed || x.ConfirmedAt > occurredAt))
            .OrderBy(x => x.Status == DoseStatus.Confirmed)
            .ThenBy(x => x.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (dose is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeviceEventMatch(null, null);
        }

        var result = new DoseStateMachine(clock).ApplyLidOpened(dose, deviceEvent.CompartmentId, occurredAt);
        if (result.Outcome != DoseTransitionOutcome.Confirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeviceEventMatch(null, null);
        }

        deviceEvent.MatchedDoseInstanceId = dose.Id;
        if (result.Reason == DoseTransitionReason.LateConfirmation)
        {
            // D-24b: cảnh báo của dose được đánh dấu đã giải quyết cùng transaction; không gửi email mới.
            var now = clock.UtcNow;
            var alerts = await db.Alerts
                .Where(x => x.DoseInstanceId == dose.Id && x.ResolvedAt == null)
                .ToListAsync(cancellationToken);
            alerts.ForEach(x => x.ResolvedAt = now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DeviceEventMatch(dose.Id, dose.Status);
    }

    private static bool IsDeadlock(Exception? ex)
    {
        // EF bọc SqlException trong DbUpdateException/InvalidOperationException nên phải duyệt cả chuỗi inner.
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is SqlException { Number: 1205 })
            {
                return true;
            }
        }

        return false;
    }
}
