using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Schedules;

namespace SmartMedBox.Infrastructure.Devices;

public sealed record DeviceEventMatch(Guid? DoseInstanceId, DoseStatus? DoseStatus);

public sealed class DeviceEventMatchingService(AppDbContext db, IClock clock)
{
    public async Task<DeviceEventMatch> MatchAsync(
        Guid deviceId,
        string eventId,
        CancellationToken cancellationToken = default)
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

        var dose = await db.DoseInstances
            .Where(x => x.DeviceId == deviceId
                && x.CompartmentId == deviceEvent.CompartmentId
                && x.ScheduledAt <= deviceEvent.OccurredAt
                && x.DeadlineAt >= deviceEvent.OccurredAt)
            .OrderBy(x => x.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (dose is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DeviceEventMatch(null, null);
        }

        var result = new DoseStateMachine(clock).ApplyLidOpened(
            dose,
            deviceEvent.CompartmentId,
            deviceEvent.OccurredAt);
        if (result.Outcome == DoseTransitionOutcome.Confirmed)
        {
            deviceEvent.MatchedDoseInstanceId = dose.Id;
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        return new DeviceEventMatch(
            result.Outcome == DoseTransitionOutcome.Confirmed ? dose.Id : null,
            result.Outcome == DoseTransitionOutcome.Confirmed ? dose.Status : null);
    }
}
