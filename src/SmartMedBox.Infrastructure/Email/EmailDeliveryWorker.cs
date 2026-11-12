using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Email;

public sealed class EmailDeliveryWorkerOptions
{
    /// <summary>D-13: claim lease (<c>LockedUntil</c>) of 2 minutes by default.</summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
}

public enum EmailDeliveryResult { NoDelivery, Sent, Retried, Exhausted, LeaseLost }

public sealed class EmailDeliveryWorker(AppDbContext db, IClock clock, IEmailSender sender, IEmailRetryPolicy retryPolicy, EmailDeliveryWorkerOptions options)
{
    private readonly TimeSpan _leaseDuration = Validate(options).LeaseDuration;

    public async Task<EmailDeliveryResult> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var leaseUntil = now.Add(_leaseDuration);
        var id = await db.EmailDeliveries.AsNoTracking()
            .Where(x => x.Status == EmailDeliveryStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now) && (x.LockedUntil == null || x.LockedUntil <= now))
            .OrderBy(x => x.NextAttemptAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (id is null) return EmailDeliveryResult.NoDelivery;

        var claimed = await db.EmailDeliveries.Where(x => x.Id == id && x.Status == EmailDeliveryStatus.Pending && (x.LockedUntil == null || x.LockedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, leaseUntil), cancellationToken);
        if (claimed == 0) return EmailDeliveryResult.LeaseLost;

        var payload = await (from delivery in db.EmailDeliveries.AsNoTracking()
            join recipient in db.AlertRecipients on delivery.AlertRecipientId equals recipient.Id
            join alert in db.Alerts on recipient.AlertId equals alert.Id
            join user in db.ApplicationUsers on recipient.RecipientUserId equals user.Id
            where delivery.Id == id select new { delivery.Id, delivery.AttemptNumber, user.Email, alert.Message }).SingleAsync(cancellationToken);
        try
        {
            var messageId = await sender.SendAsync(EmailTemplates.DoseAlert(payload.Email, payload.Message), cancellationToken);
            var sent = await db.EmailDeliveries.Where(x => x.Id == payload.Id && x.Status == EmailDeliveryStatus.Pending && x.LockedUntil == leaseUntil)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, EmailDeliveryStatus.Sent).SetProperty(x => x.SentAt, now)
                    .SetProperty(x => x.ProviderMessageId, messageId).SetProperty(x => x.LockedUntil, (DateTimeOffset?)null), cancellationToken);
            return sent == 1 ? EmailDeliveryResult.Sent : EmailDeliveryResult.LeaseLost;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return await RecordFailureAsync(payload.Id, payload.AttemptNumber, leaseUntil, now, cancellationToken); }
    }

    private async Task<EmailDeliveryResult> RecordFailureAsync(Guid id, int attemptNumber, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var exhausted = attemptNumber >= retryPolicy.MaxAttempts;
        var updated = await db.EmailDeliveries.Where(x => x.Id == id && x.Status == EmailDeliveryStatus.Pending && x.LockedUntil == leaseUntil)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, exhausted ? EmailDeliveryStatus.Exhausted : EmailDeliveryStatus.Failed).SetProperty(x => x.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        if (updated == 0) { await transaction.RollbackAsync(cancellationToken); return EmailDeliveryResult.LeaseLost; }
        if (!exhausted)
        {
            var recipientId = await db.EmailDeliveries.Where(x => x.Id == id).Select(x => x.AlertRecipientId).SingleAsync(cancellationToken);
            db.EmailDeliveries.Add(new EmailDelivery { Id = Guid.NewGuid(), AlertRecipientId = recipientId, AttemptNumber = attemptNumber + 1, Status = EmailDeliveryStatus.Pending, NextAttemptAt = retryPolicy.NextAttemptAt(now, attemptNumber) });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return exhausted ? EmailDeliveryResult.Exhausted : EmailDeliveryResult.Retried;
    }

    private static EmailDeliveryWorkerOptions Validate(EmailDeliveryWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.LeaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        return options;
    }
}
