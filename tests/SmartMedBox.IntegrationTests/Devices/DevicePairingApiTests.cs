using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DevicePairingApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string PairUrl = "/api/devices/pair";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task Pair_ValidCode_AssignsOwnerConsumesCodeAndAudits()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        var user = await TestUsers.SeedAsync(fixture);
        using var client = await LoginWithCsrfAsync(factory, user);

        // Nhãn in dạng XXXX-XXXX, chữ thường: server chuẩn hóa trước khi băm.
        using var response = await PairAsync(client, " " + PairingCode.FormatForLabel(device.PairingCode.Code).ToLowerInvariant() + " ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var body = (await ReadJsonAsync(response)).GetProperty("device");
        Assert.Equal(device.Provisioned.Device.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(device.Provisioned.Device.SerialNumber, body.GetProperty("serialNumber").GetString());
        Assert.Equal(Now, body.GetProperty("pairedAt").GetDateTimeOffset());
        Assert.Equal(0, body.GetProperty("scheduleVersion").GetInt32());
        var compartments = body.GetProperty("compartments").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], compartments.Select(x => x.GetProperty("slotNumber").GetInt32()));
        Assert.Equal(["Sáng", "Trưa", "Tối"], compartments.Select(x => x.GetProperty("name").GetString()));

        // Không lộ khóa, hash hay DeviceId kỹ thuật.
        Assert.DoesNotContain(device.DeviceApiKey, json);
        Assert.DoesNotContain(device.Provisioned.Device.DeviceApiKeyHash, json);
        Assert.DoesNotContain(device.Provisioned.Device.DeviceId, json);
        Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);

        await using var db = fixture.CreateContext();
        var deviceKey = device.Provisioned.Device.Id;
        Assert.Equal(user.Id, (await db.Devices.SingleAsync(x => x.Id == deviceKey)).OwnerUserId);
        var code = await db.DevicePairingCodes.SingleAsync(x => x.DeviceId == deviceKey);
        Assert.Equal(Now, code.UsedAt);
        Assert.Equal(user.Id, code.UsedByUserId);
        var audit = await db.AuditLogs.SingleAsync(x => x.DeviceId == deviceKey && x.Action == AuditActions.DevicePaired);
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Equal(nameof(Device), audit.EntityType);
        Assert.DoesNotContain(device.PairingCode.Code, audit.Details);
        Assert.DoesNotContain(code.CodeHash, audit.Details);
    }

    [Fact]
    public async Task Pair_RescanUsedCode_Returns409PairingCodeUsedAndKeepsOwner()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        var other = await TestUsers.SeedAsync(fixture);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        using var otherClient = await LoginWithCsrfAsync(factory, other);
        (await PairAsync(ownerClient, device.PairingCode.Code)).EnsureSuccessStatusCode();

        await AssertCodeAsync(await PairAsync(ownerClient, device.PairingCode.Code), HttpStatusCode.Conflict, "PAIRING_CODE_USED");
        await AssertCodeAsync(await PairAsync(otherClient, device.PairingCode.Code), HttpStatusCode.Conflict, "PAIRING_CODE_USED");

        await using var db = fixture.CreateContext();
        Assert.Equal(owner.Id, (await db.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id)).OwnerUserId);
    }

    [Fact]
    public async Task Pair_ExpiredCode_Returns410AndLeavesDeviceUnpaired()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        using var client = await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture));
        _clock.UtcNow = device.PairingCode.ExpiresAt;

        await AssertCodeAsync(await PairAsync(client, device.PairingCode.Code), HttpStatusCode.Gone, "PAIRING_CODE_EXPIRED");

        await AssertUnpairedAsync(device);
    }

    [Fact]
    public async Task Pair_UnknownOrMalformedCode_ReturnsSame400PairingCodeInvalid()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        using var client = await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture));
        var unknown = PairingCode.Create();
        Assert.NotEqual(device.PairingCode.Code, unknown);

        var details = new List<string?>();
        foreach (var code in new[] { unknown, "ABC", "UUUUUUUU", device.PairingCode.Code + "0" })
        {
            using var response = await PairAsync(client, code);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await ReadJsonAsync(response);
            Assert.Equal("PAIRING_CODE_INVALID", body.GetProperty("code").GetString());
            details.Add(body.GetProperty("detail").GetString());
        }

        Assert.Single(details.Distinct());
        await AssertUnpairedAsync(device);
    }

    [Fact]
    public async Task Pair_EmailNotVerified_Returns403AndDoesNotConsumeCode()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        var user = await TestUsers.SeedAsync(fixture);
        using var client = await LoginWithCsrfAsync(factory, user);
        await using (var db = fixture.CreateContext())
        {
            await db.ApplicationUsers.Where(x => x.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailVerified, false));
        }

        await AssertCodeAsync(await PairAsync(client, device.PairingCode.Code), HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");

        await AssertUnpairedAsync(device);
    }

    [Fact]
    public async Task Pair_UserAlreadyOwnsDevice_Returns409AndDoesNotConsumeCode()
    {
        using var factory = CreateFactory();
        var first = await ProvisionAsync(factory);
        var second = await ProvisionAsync(factory);
        using var client = await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture));
        (await PairAsync(client, first.PairingCode.Code)).EnsureSuccessStatusCode();

        await AssertCodeAsync(await PairAsync(client, second.PairingCode.Code), HttpStatusCode.Conflict, "USER_ALREADY_OWNS_DEVICE");

        await AssertUnpairedAsync(second);
    }

    [Fact]
    public async Task Pair_DeviceAlreadyPaired_Returns409AndKeepsOwner()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        await using (var db = fixture.CreateContext())
        {
            // Hộp đã có chủ nhưng còn mã chưa dùng (dữ liệu lệch): vẫn không được đổi chủ.
            await db.Devices.Where(x => x.Id == device.Provisioned.Device.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerUserId, owner.Id));
        }

        using var client = await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture));

        await AssertCodeAsync(await PairAsync(client, device.PairingCode.Code), HttpStatusCode.Conflict, "DEVICE_ALREADY_PAIRED");

        await using var verify = fixture.CreateContext();
        Assert.Equal(owner.Id, (await verify.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id)).OwnerUserId);
        Assert.Null((await verify.DevicePairingCodes.SingleAsync(x => x.DeviceId == device.Provisioned.Device.Id)).UsedAt);
    }

    [Fact]
    public async Task Pair_ConcurrentRequestsSameCode_ExactlyOneSucceeds()
    {
        using var factory = CreateFactory();
        var device = await ProvisionAsync(factory);
        var clients = new List<HttpClient>();
        for (var i = 0; i < 4; i++)
        {
            clients.Add(await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture)));
        }

        var responses = await Task.WhenAll(clients.Select(c => PairAsync(c, device.PairingCode.Code)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            await AssertCodeAsync(rejected, HttpStatusCode.Conflict, "PAIRING_CODE_USED");
        }

        await using var db = fixture.CreateContext();
        var deviceKey = device.Provisioned.Device.Id;
        var ownerId = (await db.Devices.SingleAsync(x => x.Id == deviceKey)).OwnerUserId;
        Assert.Equal(ownerId, (await db.DevicePairingCodes.SingleAsync(x => x.DeviceId == deviceKey)).UsedByUserId);
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.DeviceId == deviceKey && x.Action == AuditActions.DevicePaired));
        clients.ForEach(c => c.Dispose());
    }

    [Fact]
    public async Task Pair_SameUserConcurrentDifferentDevices_OwnsExactlyOne()
    {
        using var factory = CreateFactory();
        var devices = new List<ProvisioningResult>();
        for (var i = 0; i < 3; i++)
        {
            devices.Add(await ProvisionAsync(factory));
        }

        var user = await TestUsers.SeedAsync(fixture);
        using var client = await LoginWithCsrfAsync(factory, user);

        var responses = await Task.WhenAll(devices.Select(d => PairAsync(client, d.PairingCode.Code)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            await AssertCodeAsync(rejected, HttpStatusCode.Conflict, "USER_ALREADY_OWNS_DEVICE");
        }

        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Devices.CountAsync(x => x.OwnerUserId == user.Id));
        var keys = devices.Select(d => d.Provisioned.Device.Id).ToList();
        Assert.Equal(1, await db.DevicePairingCodes.CountAsync(x => keys.Contains(x.DeviceId) && x.UsedAt != null));
    }

    [Fact]
    public async Task Pair_OverRateLimit_Returns429WithRetryAfter()
    {
        using var factory = CreateFactory().WithWebHostBuilder(b => b.UseSetting("RateLimiting:Pair:PermitLimit", "2"));
        using var client = await LoginWithCsrfAsync(factory, await TestUsers.SeedAsync(fixture));

        for (var i = 0; i < 2; i++)
        {
            await AssertCodeAsync(await PairAsync(client, PairingCode.Create()), HttpStatusCode.BadRequest, "PAIRING_CODE_INVALID");
        }

        using var limited = await PairAsync(client, PairingCode.Create());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.Equal("RATE_LIMITED", await ReadCodeAsync(limited));
    }

    [Fact]
    public async Task Pair_Anonymous_Returns401()
    {
        using var factory = CreateFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await PairAsync(client, PairingCode.Create());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static Task<HttpResponseMessage> PairAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync(PairUrl, new { code });

    internal static async Task<ProvisioningResult> ProvisionAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        return await new DeviceOnboardingService(
                services.GetRequiredService<AppDbContext>(),
                services.GetRequiredService<ITokenHasher>(),
                services.GetRequiredService<IClock>())
            .ProvisionAsync($"serial-{Guid.NewGuid():N}");
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }));

    private async Task AssertUnpairedAsync(ProvisioningResult device)
    {
        await using var db = fixture.CreateContext();
        Assert.Null((await db.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id)).OwnerUserId);
        Assert.Null((await db.DevicePairingCodes.SingleAsync(x => x.DeviceId == device.Provisioned.Device.Id)).UsedAt);
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await ReadCodeAsync(response));
        }
    }
}
