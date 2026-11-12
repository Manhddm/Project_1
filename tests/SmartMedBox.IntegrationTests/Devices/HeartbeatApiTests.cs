using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class HeartbeatApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/device/heartbeat";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task Heartbeat_UnpairedDevice_ReturnsUnpairedAndUpdatesLastSeenAt()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(HeartbeatRequest(device));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.Equal("Unpaired", body.RootElement.GetProperty("pairingState").GetString());
        Assert.Equal(Now, body.RootElement.GetProperty("serverTimeUtc").GetDateTimeOffset());
        Assert.Equal(0, body.RootElement.GetProperty("scheduleVersion").GetInt32());
        Assert.True(body.RootElement.GetProperty("nextHeartbeatSeconds").GetInt32() > 0);
        Assert.DoesNotContain(device.DeviceApiKey, json);
        Assert.Equal(Now, await GetLastSeenAtAsync(device));
    }

    [Fact]
    public async Task Heartbeat_PairedDevice_ReturnsPairedWithScheduleVersion()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        await using (var db = fixture.CreateContext())
        {
            var stored = await db.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id);
            stored.OwnerUserId = owner.Id;
            stored.ScheduleVersion = 4;
            await db.SaveChangesAsync();
        }

        using var client = TestUsers.CreateClient(factory);
        using var response = await client.SendAsync(HeartbeatRequest(device));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Paired", body.RootElement.GetProperty("pairingState").GetString());
        Assert.Equal(4, body.RootElement.GetProperty("scheduleVersion").GetInt32());
    }

    [Fact]
    public async Task Heartbeat_RepeatedCalls_OnlyAdvanceLastSeenAt()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        (await client.SendAsync(HeartbeatRequest(device))).EnsureSuccessStatusCode();
        _clock.UtcNow = Now.AddMinutes(1);
        (await client.SendAsync(HeartbeatRequest(device))).EnsureSuccessStatusCode();

        Assert.Equal(Now.AddMinutes(1), await GetLastSeenAtAsync(device));
        await using var db = fixture.CreateContext();
        var stored = await db.Devices.AsNoTracking().SingleAsync(x => x.Id == device.Provisioned.Device.Id);
        Assert.Null(stored.OwnerUserId);
        Assert.Equal(0, stored.ScheduleVersion);
        Assert.Equal(device.Provisioned.Device.DeviceApiKeyHash, stored.DeviceApiKeyHash);
        var window = await db.DeviceConnectivityWindows.AsNoTracking().SingleAsync(x => x.DeviceId == stored.Id);
        Assert.Equal((Now, Now.AddMinutes(1)), (window.OnlineFrom, window.LastSeenAt));
    }

    [Fact]
    public async Task Heartbeat_WrongKey_Returns401AndDoesNotTouchLastSeenAt()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var request = DeviceAuthenticationTests.DeviceRequest(
            device.Provisioned.Device.DeviceId, SmartMedBox.Domain.Devices.DeviceApiKey.Create(), Url);
        request.Content = JsonContent.Create(new { firmwareVersion = "0.1.0" });
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await GetLastSeenAtAsync(device));
        await using var db = fixture.CreateContext();
        Assert.False(await db.DeviceConnectivityWindows.AnyAsync(x => x.DeviceId == device.Provisioned.Device.Id));
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }));

    private static HttpRequestMessage HeartbeatRequest(ProvisioningResult device)
    {
        var request = DeviceAuthenticationTests.DeviceRequest(device.Provisioned.Device.DeviceId, device.DeviceApiKey, Url);
        request.Content = JsonContent.Create(new
        {
            firmwareVersion = "0.1.0",
            rtcTimeUtc = Now.AddSeconds(-2),
            pendingEventCount = 3,
        });
        return request;
    }

    private async Task<DateTimeOffset?> GetLastSeenAtAsync(ProvisioningResult device)
    {
        await using var db = fixture.CreateContext();
        return await db.Devices.AsNoTracking()
            .Where(x => x.Id == device.Provisioned.Device.Id)
            .Select(x => x.LastSeenAt)
            .SingleAsync();
    }
}
