using System.Net;
using System.Text.Json;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceAccessTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task GetRole_OwnerActiveRelativeRevokedStranger_ResolvesPerDevice()
    {
        var setup = await SeedAsync();
        await using var db = fixture.CreateContext();
        var service = new DeviceAccessService(db);

        Assert.Equal(DeviceAccessRole.Owner, await service.GetRoleAsync(setup.Owner.Id, setup.DeviceId));
        Assert.Equal(DeviceAccessRole.Relative, await service.GetRoleAsync(setup.Relative.Id, setup.DeviceId));
        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.RevokedRelative.Id, setup.DeviceId));
        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.Stranger.Id, setup.DeviceId));
    }

    [Fact]
    public async Task GetRole_OtherDeviceUnpairedOrMissing_ReturnsNone()
    {
        var setup = await SeedAsync();
        var other = await SeedAsync();
        var unpairedDeviceId = await SeedDeviceAsync(ownerUserId: null);
        await using var db = fixture.CreateContext();
        var service = new DeviceAccessService(db);

        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.Owner.Id, other.DeviceId));
        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.Relative.Id, other.DeviceId));
        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.Owner.Id, unpairedDeviceId));
        Assert.Equal(DeviceAccessRole.None, await service.GetRoleAsync(setup.Owner.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Endpoints_AuthorizationMatrix_OwnerManagesRelativeReadsOthersForbidden()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var url = $"/api/test-probe/devices/{setup.DeviceId}";

        using (var owner = await TestUsers.LoginAsync(factory, setup.Owner))
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync(url, null)).StatusCode);
        }

        using (var relative = await TestUsers.LoginAsync(factory, setup.Relative))
        {
            Assert.Equal(HttpStatusCode.OK, (await relative.GetAsync(url)).StatusCode);
            await AssertForbiddenAsync(await relative.PutAsync(url, null));
        }

        foreach (var outsider in new[] { setup.RevokedRelative, setup.Stranger })
        {
            using var client = await TestUsers.LoginAsync(factory, outsider);
            await AssertForbiddenAsync(await client.GetAsync(url));
            await AssertForbiddenAsync(await client.PutAsync(url, null));
        }

        using var anonymous = TestUsers.CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Endpoints_ClientSuppliedUserIdOfOwner_IsIgnored()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var stranger = await TestUsers.LoginAsync(factory, setup.Stranger);
        var url = $"/api/test-probe/devices/{setup.DeviceId}?userId={setup.Owner.Id}";

        await AssertForbiddenAsync(await stranger.GetAsync(url));
        await AssertForbiddenAsync(await stranger.PutAsync(url, null));
    }

    [Fact]
    public async Task Endpoints_MissingDeviceAndForeignDevice_ReturnSameForbidden()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var stranger = await TestUsers.LoginAsync(factory, setup.Stranger);

        using var foreign = await stranger.GetAsync($"/api/test-probe/devices/{setup.DeviceId}");
        using var missing = await stranger.GetAsync($"/api/test-probe/devices/{Guid.NewGuid()}");

        await AssertForbiddenAsync(foreign);
        await AssertForbiddenAsync(missing);
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", document.RootElement.GetProperty("code").GetString());
    }

    private async Task<Setup> SeedAsync()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var relative = await TestUsers.SeedAsync(fixture);
        var revokedRelative = await TestUsers.SeedAsync(fixture);
        var stranger = await TestUsers.SeedAsync(fixture);
        var deviceId = await SeedDeviceAsync(owner.Id);

        await using var db = fixture.CreateContext();
        db.CareRelationships.AddRange(
            new CareRelationship
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                RelativeUserId = relative.Id,
                Status = RelationshipStatus.Active,
                AcceptedAt = DateTimeOffset.UtcNow,
            },
            new CareRelationship
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                RelativeUserId = revokedRelative.Id,
                Status = RelationshipStatus.Revoked,
                AcceptedAt = DateTimeOffset.UtcNow.AddDays(-2),
                RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        await db.SaveChangesAsync();

        return new Setup(deviceId, owner, relative, revokedRelative, stranger);
    }

    private async Task<Guid> SeedDeviceAsync(Guid? ownerUserId)
    {
        await using var db = fixture.CreateContext();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceId = $"esp32-{Guid.NewGuid():N}",
            SerialNumber = $"SN-{Guid.NewGuid():N}",
            DeviceApiKeyHash = Guid.NewGuid().ToString("N"),
            OwnerUserId = ownerUserId,
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }

    private sealed record Setup(
        Guid DeviceId,
        ApplicationUser Owner,
        ApplicationUser Relative,
        ApplicationUser RevokedRelative,
        ApplicationUser Stranger);
}
