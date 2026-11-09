using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class MyDeviceApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/devices/mine";

    [Fact]
    public async Task Mine_Owner_ReturnsDeviceCompartmentsAndNoSecrets()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var device = await DevicePairingApiTests.ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        using var client = await LoginWithCsrfAsync(factory, owner);
        (await DevicePairingApiTests.PairAsync(client, device.PairingCode.Code)).EnsureSuccessStatusCode();
        var lastSeen = new DateTimeOffset(2026, 11, 9, 9, 31, 12, TimeSpan.Zero);
        await using (var db = fixture.CreateContext())
        {
            await db.Devices.Where(x => x.Id == device.Provisioned.Device.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ScheduleVersion, 4)
                .SetProperty(x => x.LastSeenAt, lastSeen));
        }

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var body = await ReadJsonAsync(response);
        Assert.Equal(device.Provisioned.Device.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(device.Provisioned.Device.SerialNumber, body.GetProperty("serialNumber").GetString());
        Assert.Equal(4, body.GetProperty("scheduleVersion").GetInt32());
        Assert.Equal(lastSeen, body.GetProperty("lastSeenAt").GetDateTimeOffset());
        // Ngưỡng online/offline là [TBD-D12]: không kết luận.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("isOnline").ValueKind);
        var compartments = body.GetProperty("compartments").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], compartments.Select(x => x.GetProperty("slotNumber").GetInt32()));
        Assert.All(compartments, x => Assert.True(x.GetProperty("isEnabled").GetBoolean()));
        Assert.DoesNotContain(device.DeviceApiKey, json);
        Assert.DoesNotContain(device.Provisioned.Device.DeviceApiKeyHash, json);
        Assert.DoesNotContain(device.Provisioned.Device.DeviceId, json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mine_RelativeOrStranger_Returns404DeviceNotFound()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var owner = await TestUsers.SeedAsync(fixture);
        var deviceId = await SeedDeviceAsync(fixture, owner.Id);
        var relative = await TestUsers.SeedAsync(fixture);
        await SeedRelationshipAsync(fixture, deviceId, relative.Id);
        var stranger = await TestUsers.SeedAsync(fixture);

        foreach (var user in new[] { relative, stranger })
        {
            using var client = await TestUsers.LoginAsync(factory, user);
            using var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("DEVICE_NOT_FOUND", await ReadCodeAsync(response));
        }
    }

    [Fact]
    public async Task Mine_Anonymous_Returns401()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
