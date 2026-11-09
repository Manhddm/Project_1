using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceAccessTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Password = "P@ssw0rd!2026";

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

        using (var owner = await LoginAsync(factory, setup.Owner))
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync(url, null)).StatusCode);
        }

        using (var relative = await LoginAsync(factory, setup.Relative))
        {
            Assert.Equal(HttpStatusCode.OK, (await relative.GetAsync(url)).StatusCode);
            await AssertForbiddenAsync(await relative.PutAsync(url, null));
        }

        foreach (var outsider in new[] { setup.RevokedRelative, setup.Stranger })
        {
            using var client = await LoginAsync(factory, outsider);
            await AssertForbiddenAsync(await client.GetAsync(url));
            await AssertForbiddenAsync(await client.PutAsync(url, null));
        }

        using var anonymous = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Endpoints_ClientSuppliedUserIdOfOwner_IsIgnored()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var stranger = await LoginAsync(factory, setup.Stranger);
        var url = $"/api/test-probe/devices/{setup.DeviceId}?userId={setup.Owner.Id}";

        await AssertForbiddenAsync(await stranger.GetAsync(url));
        await AssertForbiddenAsync(await stranger.PutAsync(url, null));
    }

    [Fact]
    public async Task Endpoints_MissingDeviceAndForeignDevice_ReturnSameForbidden()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var stranger = await LoginAsync(factory, setup.Stranger);

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

    private static HttpClient CreateClient(TestApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<HttpClient> LoginAsync(TestApplicationFactory factory, ApplicationUser user)
    {
        var client = CreateClient(factory);
        using var csrf = await client.GetAsync("/api/test-probe/csrf");
        csrf.EnsureSuccessStatusCode();
        using var csrfBody = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = Password }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfBody.RootElement.GetProperty("token").GetString());
        using var login = await client.SendAsync(request);
        login.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Setup> SeedAsync()
    {
        var owner = await SeedUserAsync();
        var relative = await SeedUserAsync();
        var revokedRelative = await SeedUserAsync();
        var stranger = await SeedUserAsync();
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

    private async Task<ApplicationUser> SeedUserAsync()
    {
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = new PasswordHasherService().Hash(Password),
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private sealed record Setup(
        Guid DeviceId,
        ApplicationUser Owner,
        ApplicationUser Relative,
        ApplicationUser RevokedRelative,
        ApplicationUser Stranger);
}
