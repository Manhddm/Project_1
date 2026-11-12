using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

/// <summary>D-12 (API_CONTRACT §1.7): offline after this long without a heartbeat.</summary>
public sealed class DeviceConnectivityOptions
{
    public TimeSpan OfflineThreshold { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsOnline(DateTimeOffset? lastSeenAt, DateTimeOffset now) =>
        lastSeenAt is { } seenAt && seenAt >= now - OfflineThreshold;
}

/// <summary>Single source of truth for device connectivity (D-12): heartbeat windows and online-at-deadline.</summary>
public sealed class DeviceConnectivityService(AppDbContext db, IOptions<DeviceConnectivityOptions> options)
{
    private readonly TimeSpan _threshold = options.Value.OfflineThreshold;

    public async Task RecordHeartbeatAsync(Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Updating the device row first takes its row lock, so concurrent heartbeats of one device serialize here.
        await db.Devices.Where(x => x.Id == deviceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, x => x.LastSeenAt > now ? x.LastSeenAt : now), cancellationToken);

        var latest = await db.DeviceConnectivityWindows.AsNoTracking()
            .Where(x => x.DeviceId == deviceId)
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new { x.Id, x.LastSeenAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null && now - latest.LastSeenAt <= _threshold)
        {
            await db.DeviceConnectivityWindows.Where(x => x.Id == latest.Id && x.LastSeenAt < now)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }
        else
        {
            db.DeviceConnectivityWindows.Add(new DeviceConnectivityWindow
            {
                Id = Guid.NewGuid(), DeviceId = deviceId, OnlineFrom = now, LastSeenAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Online at <paramref name="deadlineAt"/> iff a window covers it; reconnecting later does not count.</summary>
    public Task<bool> WasOnlineAtAsync(Guid deviceId, DateTimeOffset deadlineAt, CancellationToken cancellationToken = default)
    {
        var from = deadlineAt - _threshold;
        return db.DeviceConnectivityWindows.AnyAsync(
            x => x.DeviceId == deviceId && x.OnlineFrom <= deadlineAt && x.LastSeenAt >= from,
            cancellationToken);
    }
}
