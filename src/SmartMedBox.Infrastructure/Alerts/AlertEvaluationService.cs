using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Infrastructure.Alerts;

public sealed record AlertEvaluationResult(int EvaluatedCount, int CreatedCount, int SkippedCount);

public sealed class AlertEvaluationService(AppDbContext db, IClock clock, DeviceConnectivityService connectivity)
{
    public async Task<AlertEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var doseIds = await db.DoseInstances.AsNoTracking()
            .Where(x => x.Status == DoseStatus.Pending && x.DeadlineAt < now) // window [ScheduledAt, DeadlineAt] is inclusive
            .OrderBy(x => x.DeadlineAt).Select(x => x.Id).ToListAsync(cancellationToken);
        var created = 0;
        foreach (var doseId in doseIds)
        {
            if (await EvaluateDoseAsync(doseId, now, cancellationToken)) created++;
        }

        return new AlertEvaluationResult(doseIds.Count, created, doseIds.Count - created);
    }

    private async Task<bool> EvaluateDoseAsync(Guid doseId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var dose = await db.DoseInstances.AsNoTracking()
            .Where(x => x.Id == doseId && x.Status == DoseStatus.Pending && x.DeadlineAt < now)
            .Select(x => new { x.Id, x.DeviceId, x.DeadlineAt }).SingleOrDefaultAsync(cancellationToken);
        if (dose is null) return false;

        var device = await db.Devices.AsNoTracking().Where(x => x.Id == dose.DeviceId)
            .Select(x => new { x.OwnerUserId }).SingleAsync(cancellationToken);
        var online = await connectivity.WasOnlineAtAsync(dose.DeviceId, dose.DeadlineAt, cancellationToken);
        var status = online ? DoseStatus.Overdue : DoseStatus.Unknown;
        var alertType = online ? AlertType.OverdueUnconfirmed : AlertType.UnconfirmedOffline;
        var message = online ? "Lần uống đã quá hạn và chưa được xác nhận: nắp ngăn đúng chưa được mở trong thời gian chờ." : "Không thể xác nhận do mất kết nối.";

        var claimed = await db.DoseInstances.Where(x => x.Id == dose.Id && x.Status == DoseStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), cancellationToken);
        if (claimed == 0) return false;

        var recipientIds = await db.CareRelationships.AsNoTracking()
            .Where(x => x.DeviceId == dose.DeviceId && x.Status == RelationshipStatus.Active)
            .Select(x => x.RelativeUserId).ToListAsync(cancellationToken);
        if (device.OwnerUserId is { } ownerUserId) recipientIds.Add(ownerUserId);
        var recipients = await db.ApplicationUsers.AsNoTracking()
            .Where(x => recipientIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id }).ToListAsync(cancellationToken);

        var alert = new Alert { Id = Guid.NewGuid(), DoseInstanceId = dose.Id, AlertType = alertType, Message = message, CreatedAt = now };
        db.Alerts.Add(alert);
        foreach (var recipient in recipients.DistinctBy(x => x.Id))
        {
            var alertRecipient = new AlertRecipient { Id = Guid.NewGuid(), AlertId = alert.Id, RecipientUserId = recipient.Id, Channel = "Email" };
            db.AlertRecipients.Add(alertRecipient);
            db.EmailDeliveries.Add(new EmailDelivery { Id = Guid.NewGuid(), AlertRecipientId = alertRecipient.Id, AttemptNumber = 1, Status = EmailDeliveryStatus.Pending, NextAttemptAt = now });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
