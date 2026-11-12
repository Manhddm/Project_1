using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Schedules;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class ScheduleSyncApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task ScheduleSync_OlderVersion_ReturnsFullPackageWithPerScheduleTimeZone()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var morning = await CreateScheduleAsync(box.OwnerId, Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh",
            [new ScheduleItemInput("Vitamin C", 1.5m, "viên", "Uống sau ăn"), new ScheduleItemInput("Canxi", 2m, "gói", null)],
            days: [2, 4, 6], end: new DateOnly(2026, 12, 31), grace: 15));
        var evening = await CreateScheduleAsync(box.OwnerId, Input(3, new TimeOnly(21, 30), "Europe/Paris",
            [new ScheduleItemInput("Paracetamol", 1m, "viên", "Trước ngủ")]));
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(SyncRequest(box.Device, 0));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(evening.ScheduleVersion, root.GetProperty("version").GetInt32());
        Assert.Equal(Now, root.GetProperty("serverTimeUtc").GetDateTimeOffset());
        Assert.Equal(Now, root.GetProperty("generatedAtUtc").GetDateTimeOffset());
        Assert.False(root.TryGetProperty("timeZoneId", out _));

        var schedules = root.GetProperty("schedules").EnumerateArray().ToList();
        Assert.Equal(2, schedules.Count);
        var first = schedules.Single(x => x.GetProperty("id").GetGuid() == morning.ScheduleId);
        Assert.Equal(1, first.GetProperty("compartmentSlot").GetInt32());
        Assert.Equal("07:00", first.GetProperty("timeOfDay").GetString());
        Assert.Equal([2, 4, 6], first.GetProperty("daysOfWeek").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal("2026-10-01", first.GetProperty("startDate").GetString());
        Assert.Equal("2026-12-31", first.GetProperty("endDate").GetString());
        Assert.Equal(15, first.GetProperty("gracePeriodMinutes").GetInt32());
        Assert.Equal("Asia/Ho_Chi_Minh", first.GetProperty("timeZoneId").GetString());
        Assert.Equal(
            [("Canxi", 2m, "gói", ""), ("Vitamin C", 1.5m, "viên", "Uống sau ăn")],
            first.GetProperty("items").EnumerateArray().Select(i => (
                i.GetProperty("medicineName").GetString(),
                i.GetProperty("quantity").GetDecimal(),
                i.GetProperty("unit").GetString(),
                i.GetProperty("instructions").GetString())));

        var second = schedules.Single(x => x.GetProperty("id").GetGuid() == evening.ScheduleId);
        Assert.Equal(3, second.GetProperty("compartmentSlot").GetInt32());
        Assert.Equal("21:30", second.GetProperty("timeOfDay").GetString());
        Assert.Equal("Europe/Paris", second.GetProperty("timeZoneId").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("endDate").ValueKind);

        // Không lộ khóa/hash hay dữ liệu chủ hộp.
        var raw = root.GetRawText();
        Assert.DoesNotContain(box.Device.DeviceApiKey, raw);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(box.OwnerId.ToString(), raw);
    }

    [Fact]
    public async Task ScheduleSync_SameVersion_Returns204()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var created = await CreateScheduleAsync(box.OwnerId, Input(2, new TimeOnly(12, 0), "Asia/Ho_Chi_Minh"));
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(SyncRequest(box.Device, created.ScheduleVersion));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScheduleSync_ScheduleSoftDeleted_ExcludedAndVersionIncreased()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var kept = await CreateScheduleAsync(box.OwnerId, Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh"));
        var deleted = await CreateScheduleAsync(box.OwnerId, Input(2, new TimeOnly(12, 0), "Asia/Ho_Chi_Minh"));
        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(ScheduleOutcome.Ok,
                await new ScheduleService(db, new DeviceAccessService(db), _clock).DeleteAsync(box.OwnerId, deleted.ScheduleId));
        }

        using var client = TestUsers.CreateClient(factory);
        using var response = await client.SendAsync(SyncRequest(box.Device, deleted.ScheduleVersion));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(deleted.ScheduleVersion + 1, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal([kept.ScheduleId],
            body.RootElement.GetProperty("schedules").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ScheduleSync_UnpairedDevice_Returns409DeviceNotPaired()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(SyncRequest(device, 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_NOT_PAIRED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScheduleSync_WrongApiKey_Returns401DeviceUnauthorized()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var other = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(
            SyncRequest(box.Device.Provisioned.Device.DeviceId, other.DeviceApiKey, 0));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_UNAUTHORIZED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScheduleSync_UserCookie_Returns401()
    {
        using var factory = CreateFactory();
        var owner = await TestUsers.SeedAsync(fixture);
        using var client = await TestUsers.LoginAsync(factory, owner);

        using var response = await client.SendAsync(SyncRequest(null, null, 0));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleSync_MissingVersion_Returns400ValidationError()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var request = DeviceAuthenticationTests.DeviceRequest(
            box.Device.Provisioned.Device.DeviceId, box.Device.DeviceApiKey, "/api/device/schedule-sync");
        request.Method = HttpMethod.Get;
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ScheduleSync_OtherBoxHasSchedules_NeverIncluded()
    {
        using var factory = CreateFactory();
        var mine = await ProvisionPairedAsync(factory);
        var other = await ProvisionPairedAsync(factory);
        var own = await CreateScheduleAsync(mine.OwnerId, Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh"));
        await CreateScheduleAsync(other.OwnerId, Input(1, new TimeOnly(8, 0), "Asia/Tokyo"));
        await CreateScheduleAsync(other.OwnerId, Input(2, new TimeOnly(13, 0), "Asia/Tokyo"));
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(SyncRequest(mine.Device, 0));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(own.ScheduleVersion, body.RootElement.GetProperty("version").GetInt32());
        Assert.Equal([own.ScheduleId],
            body.RootElement.GetProperty("schedules").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task ScheduleSync_EditInFlight_WaitsAndReturnsCommittedVersionWithMatchingContents()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var kept = await CreateScheduleAsync(box.OwnerId, Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh"));
        var removed = await CreateScheduleAsync(box.OwnerId, Input(2, new TimeOnly(12, 0), "Asia/Ho_Chi_Minh"));
        using var client = TestUsers.CreateClient(factory);
        var deviceKey = box.Device.Provisioned.Device.Id;

        // Mô phỏng một lần ghi lịch đang dở: đã tăng version (khóa hàng Devices) + xóa mềm, chưa commit.
        await using var writer = fixture.CreateContext();
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await writer.Devices.Where(x => x.Id == deviceKey)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScheduleVersion, x => x.ScheduleVersion + 1));
            await writer.MedicationSchedules.Where(x => x.Id == removed.ScheduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true).SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow));

            var sync = client.SendAsync(SyncRequest(box.Device, 0));
            await Task.Delay(500);
            Assert.False(sync.IsCompleted);

            await transaction.CommitAsync();
            using var response = await sync;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(removed.ScheduleVersion + 1, body.RootElement.GetProperty("version").GetInt32());
            Assert.Equal([kept.ScheduleId],
                body.RootElement.GetProperty("schedules").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        }
    }

    [Fact]
    public async Task ScheduleSync_ConcurrentEdits_EveryPackageMatchesItsVersion()
    {
        using var factory = CreateFactory();
        var box = await ProvisionPairedAsync(factory);
        var created = await CreateScheduleAsync(box.OwnerId, Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh",
            [new ScheduleItemInput("med-0", 1m, "viên", null)]));
        var expected = new ConcurrentDictionary<int, string> { [created.ScheduleVersion] = "med-0" };
        using var client = TestUsers.CreateClient(factory);
        var observed = new ConcurrentBag<(int Version, string Name)>();
        using var done = new CancellationTokenSource();

        var writer = Task.Run(async () =>
        {
            for (var i = 1; i <= 25; i++)
            {
                await using var db = fixture.CreateContext();
                var result = await new ScheduleService(db, new DeviceAccessService(db), _clock).UpdateAsync(
                    box.OwnerId, created.ScheduleId,
                    Input(1, new TimeOnly(7, 0), "Asia/Ho_Chi_Minh", [new ScheduleItemInput($"med-{i}", 1m, "viên", null)]));
                Assert.Equal(ScheduleOutcome.Ok, result.Outcome);
                expected[result.ScheduleVersion] = $"med-{i}";
            }

            await done.CancelAsync();
        });
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            while (!done.IsCancellationRequested)
            {
                using var response = await client.SendAsync(SyncRequest(box.Device, -1));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var schedule = body.RootElement.GetProperty("schedules").EnumerateArray().Single();
                observed.Add((body.RootElement.GetProperty("version").GetInt32(),
                    schedule.GetProperty("items").EnumerateArray().Single().GetProperty("medicineName").GetString()!));
            }
        })).ToList();

        await Task.WhenAll(readers.Append(writer));

        Assert.NotEmpty(observed);
        Assert.All(observed, x => Assert.Equal(expected[x.Version], x.Name));
    }

    private sealed record PairedBox(ProvisioningResult Device, Guid OwnerId);

    private static ScheduleInput Input(
        int slot,
        TimeOnly time,
        string timeZoneId,
        IReadOnlyCollection<ScheduleItemInput>? items = null,
        IReadOnlyCollection<int>? days = null,
        DateOnly? end = null,
        int grace = 30) =>
        new(slot, time, days ?? [1, 2, 3, 4, 5, 6, 7], new DateOnly(2026, 10, 1), end, grace, timeZoneId,
            items ?? [new ScheduleItemInput("Vitamin C", 1m, "viên", null)]);

    private async Task<ScheduleWriteResult> CreateScheduleAsync(Guid ownerId, ScheduleInput input)
    {
        await using var db = fixture.CreateContext();
        var result = await new ScheduleService(db, new DeviceAccessService(db), _clock).CreateAsync(ownerId, input);
        Assert.Equal(ScheduleOutcome.Ok, result.Outcome);
        return result;
    }

    private static HttpRequestMessage SyncRequest(ProvisioningResult device, int version) =>
        SyncRequest(device.Provisioned.Device.DeviceId, device.DeviceApiKey, version);

    private static HttpRequestMessage SyncRequest(string? deviceId, string? apiKey, int version)
    {
        var request = DeviceAuthenticationTests.DeviceRequest(deviceId, apiKey, $"/api/device/schedule-sync?version={version}");
        request.Method = HttpMethod.Get;
        return request;
    }

    private async Task<PairedBox> ProvisionPairedAsync(WebApplicationFactory<Program> factory)
    {
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        await using var db = fixture.CreateContext();
        var stored = await db.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id);
        stored.OwnerUserId = owner.Id;
        await db.SaveChangesAsync();
        return new PairedBox(device, owner.Id);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }));
}
