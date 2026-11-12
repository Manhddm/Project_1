using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;

namespace SmartMedBox.IntegrationTests.Devices;

/// <summary>D-12 connectivity windows (API_CONTRACT §1.7) against real SQL Server.</summary>
public sealed class DeviceConnectivityTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordHeartbeat_GapWithinFiveMinutes_ExtendsWindow()
    {
        var deviceId = await SeedDeviceAsync();

        await HeartbeatAsync(deviceId, T0);
        await HeartbeatAsync(deviceId, T0.AddMinutes(1));
        await HeartbeatAsync(deviceId, T0.AddMinutes(6)); // gap exactly 5 minutes

        var window = Assert.Single(await WindowsAsync(deviceId));
        Assert.Equal((T0, T0.AddMinutes(6)), (window.OnlineFrom, window.LastSeenAt));
        await using var db = fixture.CreateContext();
        Assert.Equal(T0.AddMinutes(6), (await db.Devices.SingleAsync(x => x.Id == deviceId)).LastSeenAt);
    }

    [Fact]
    public async Task RecordHeartbeat_GapOverFiveMinutes_OpensNewWindow()
    {
        var deviceId = await SeedDeviceAsync();

        await HeartbeatAsync(deviceId, T0);
        await HeartbeatAsync(deviceId, T0.AddMinutes(5).AddSeconds(1));

        var windows = await WindowsAsync(deviceId);
        Assert.Equal(
            [(T0, T0), (T0.AddMinutes(5).AddSeconds(1), T0.AddMinutes(5).AddSeconds(1))],
            windows.Select(x => (x.OnlineFrom, x.LastSeenAt)));
    }

    [Theory]
    [InlineData(false)] // first heartbeat ever: both would otherwise insert a window
    [InlineData(true)]  // after a long gap: both would otherwise open a new window
    public async Task RecordHeartbeat_ConcurrentHeartbeats_CreateOneWindow(bool afterGap)
    {
        var deviceId = await SeedDeviceAsync();
        if (afterGap) await HeartbeatAsync(deviceId, T0.AddHours(-1));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => HeartbeatAsync(deviceId, T0.AddMilliseconds(i)))));

        var windows = await WindowsAsync(deviceId);
        Assert.Equal(afterGap ? 2 : 1, windows.Count);
        Assert.Equal(T0.AddMilliseconds(7), windows[^1].LastSeenAt);
        Assert.True(windows[^1].OnlineFrom >= T0);
    }

    [Fact]
    public async Task WasOnlineAt_UsesWindowCoveringDeadlineNotCurrentLastSeen()
    {
        var deviceId = await SeedDeviceAsync();
        await HeartbeatAsync(deviceId, T0);
        await HeartbeatAsync(deviceId, T0.AddMinutes(10));
        await HeartbeatAsync(deviceId, T0.AddMinutes(30)); // reconnect after a 20-minute outage

        await using var db = fixture.CreateContext();
        var service = Service(db);
        Assert.True(await service.WasOnlineAtAsync(deviceId, T0.AddMinutes(15)));            // within 5 min of window end
        Assert.False(await service.WasOnlineAtAsync(deviceId, T0.AddMinutes(15).AddSeconds(1)));
        Assert.False(await service.WasOnlineAtAsync(deviceId, T0.AddMinutes(25)));            // outage
        Assert.True(await service.WasOnlineAtAsync(deviceId, T0.AddMinutes(30)));
        Assert.False(await service.WasOnlineAtAsync(deviceId, T0.AddSeconds(-1)));            // before first window
        Assert.False(await service.WasOnlineAtAsync(Guid.NewGuid(), T0));                    // never seen
    }

    private async Task HeartbeatAsync(Guid deviceId, DateTimeOffset now)
    {
        await using var db = fixture.CreateContext();
        await Service(db).RecordHeartbeatAsync(deviceId, now);
    }

    private static DeviceConnectivityService Service(Infrastructure.Data.AppDbContext db) =>
        new(db, Options.Create(new DeviceConnectivityOptions()));

    private async Task<List<DeviceConnectivityWindow>> WindowsAsync(Guid deviceId)
    {
        await using var db = fixture.CreateContext();
        return await db.DeviceConnectivityWindows.AsNoTracking()
            .Where(x => x.DeviceId == deviceId).OrderBy(x => x.OnlineFrom).ToListAsync();
    }

    private async Task<Guid> SeedDeviceAsync()
    {
        await using var db = fixture.CreateContext();
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceId = $"device-{Guid.NewGuid():N}", SerialNumber = $"serial-{Guid.NewGuid():N}",
            DeviceApiKeyHash = "not-a-real-hash",
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }
}
