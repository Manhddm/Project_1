using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceEventsBatchApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/device/events/batch";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task EventsBatch_SameBatchSentThreeTimes_StoresOneRowPerEventAndLaterDuplicate()
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);
        var events = new[] { Event("e-1", 1, "LidOpened", Now.AddMinutes(-5)), Event("e-2", 1, "LidClosed", Now.AddMinutes(-4)) };

        var first = await SendAsync(client, device, events);
        var second = await SendAsync(client, device, events);
        var third = await SendAsync(client, device, events);

        Assert.All(first, r => Assert.Equal("Accepted", r.Status));
        Assert.All(second.Concat(third), r => Assert.Equal("Duplicate", r.Status));
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.AsNoTracking().Where(x => x.DeviceId == device.Provisioned.Device.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, x => Assert.Equal(Now, x.ReceivedAt));
        Assert.All(stored, x => Assert.Null(x.MatchedDoseInstanceId));
    }

    [Fact]
    public async Task EventsBatch_ShuffledBatch_StoresSameResultAndKeepsOccurredAt()
    {
        var events = Enumerable.Range(1, 6)
            .Select(i => Event($"s-{i}", (i % 3) + 1, i % 2 == 0 ? "LidClosed" : "LidOpened", Now.AddMinutes(-i)))
            .ToArray();
        var shuffled = events.Reverse().Skip(3).Concat(events.Reverse().Take(3)).ToArray();

        var a = await SendToFreshDeviceAsync(events);
        var b = await SendToFreshDeviceAsync(shuffled);

        Assert.Equal(a, b);
        Assert.Equal(events.Select(e => ((string)e.eventId, (DateTimeOffset)e.occurredAt)).OrderBy(x => x.Item1),
            a.Select(x => (x.EventId, x.OccurredAt)));
    }

    [Fact]
    public async Task EventsBatch_MixedValidAndInvalid_StoresValidAndReportsInvalidPerElement()
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        var results = await SendAsync(client, device,
        [
            Event("ok-1", 2, "LidOpened", Now.AddMinutes(-1)),
            Event("bad-slot", 4, "LidOpened", Now.AddMinutes(-1)),
            Event("bad-slot-0", 0, "LidOpened", Now.AddMinutes(-1)),
            Event("bad-type", 1, "LidTampered", Now.AddMinutes(-1)),
            new { eventId = "bad-time", compartmentSlot = 1, eventType = "LidOpened" },
            Event("ok-2", 3, "LidClosed", Now.AddMinutes(-2)),
            Event("ok-1", 2, "LidOpened", Now.AddMinutes(-1)),
        ]);

        Assert.Equal(
            [
                ("ok-1", "Accepted", null), ("bad-slot", "Rejected", "COMPARTMENT_INVALID"),
                ("bad-slot-0", "Rejected", "COMPARTMENT_INVALID"), ("bad-type", "Rejected", "EVENT_TYPE_INVALID"),
                ("bad-time", "Rejected", "EVENT_TIME_INVALID"), ("ok-2", "Accepted", null), ("ok-1", "Duplicate", null),
            ],
            results.Select(r => (r.EventId, r.Status, r.Code)));
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.AsNoTracking()
            .Where(x => x.DeviceId == device.Provisioned.Device.Id).Select(x => x.EventId).OrderBy(x => x).ToListAsync();
        Assert.Equal(["ok-1", "ok-2"], stored);
    }

    [Fact]
    public async Task EventsBatch_UserCookieOrMissingHeaders_Returns401()
    {
        var user = await TestUsers.SeedAsync(fixture);
        using var factory = CreateFactory();
        using var cookieClient = await TestUsers.LoginAsync(factory, user);
        using var anonymous = TestUsers.CreateClient(factory);

        foreach (var client in new[] { cookieClient, anonymous })
        {
            using var request = DeviceAuthenticationTests.DeviceRequest(null, null, Url);
            request.Content = JsonContent.Create(new { events = new[] { Event("x", 1, "LidOpened", Now) } });
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using var db = fixture.CreateContext();
        Assert.False(await db.DeviceEvents.AnyAsync(x => x.EventId == "x"));
    }

    [Fact]
    public async Task EventsBatch_UnpairedDevice_Returns409DeviceNotPaired()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(Request(device, [Event("u-1", 1, "LidOpened", Now)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_NOT_PAIRED", body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task EventsBatch_SizeOutOfRange_Returns400ValidationError(int count)
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(Request(device,
            Enumerable.Range(0, count).Select(i => Event($"n-{i}", 1, "LidOpened", Now)).ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("VALIDATION_ERROR", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EventsBatch_EventIdTooLong_Returns400ValidationError()
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(Request(device, [Event(new string('a', 101), 1, "LidOpened", Now)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EventsBatch_TwoConcurrentIdenticalBatches_NoDuplicatesAndNo500()
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);
        var events = Enumerable.Range(0, 20).Select(i => Event($"c-{i}", (i % 3) + 1, "LidOpened", Now.AddSeconds(-i))).ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.SendAsync(Request(device, events))));

        var all = new List<ResultRow>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            all.AddRange(await ReadResultsAsync(response));
            response.Dispose();
        }

        foreach (var group in all.GroupBy(x => x.EventId))
        {
            Assert.Equal(["Accepted", "Duplicate"], group.Select(x => x.Status).Order());
        }

        await using var db = fixture.CreateContext();
        Assert.Equal(20, await db.DeviceEvents.CountAsync(x => x.DeviceId == device.Provisioned.Device.Id));
    }

    [Fact]
    public async Task EventsBatch_OccurredAtOutsideClockSkewWindow_RejectedEventTimeInvalidAndNotStored()
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        var results = await SendAsync(client, device,
        [
            Event("future-edge", 1, "LidOpened", Now.AddMinutes(5)),
            Event("future-over", 1, "LidOpened", Now.AddMinutes(5).AddSeconds(1)),
            Event("old-edge", 2, "LidOpened", Now.AddDays(-30)),
            Event("old-over", 2, "LidOpened", Now.AddDays(-30).AddSeconds(-1)),
        ]);

        Assert.Equal(
            [
                ("future-edge", "Accepted", null), ("future-over", "Rejected", "EVENT_TIME_INVALID"),
                ("old-edge", "Accepted", null), ("old-over", "Rejected", "EVENT_TIME_INVALID"),
            ],
            results.Select(r => (r.EventId, r.Status, r.Code)));
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.AsNoTracking()
            .Where(x => x.DeviceId == device.Provisioned.Device.Id).Select(x => x.EventId).OrderBy(x => x).ToListAsync();
        Assert.Equal(["future-edge", "old-edge"], stored);
    }

    [Fact]
    public async Task EventsBatch_ConfiguredClockSkewThresholds_AppliedFromOptions()
    {
        using var factory = CreateFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Configure<DeviceEventTimeOptions>(options =>
            {
                options.MaxFutureSkew = TimeSpan.FromMinutes(1);
                options.MaxAge = TimeSpan.FromDays(1);
            })));
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        var results = await SendAsync(client, device,
        [
            Event("cfg-future", 1, "LidOpened", Now.AddMinutes(2)),
            Event("cfg-old", 1, "LidOpened", Now.AddDays(-2)),
            Event("cfg-ok", 1, "LidOpened", Now.AddHours(-1)),
        ]);

        Assert.Equal(["Rejected", "Rejected", "Accepted"], results.Select(r => r.Status));
    }

    private sealed record ResultRow(string EventId, string Status, string? Code);

    private async Task<List<(string EventId, DateTimeOffset OccurredAt, int Slot, DeviceEventType Type)>> SendToFreshDeviceAsync(object[] events)
    {
        using var factory = CreateFactory();
        var device = await ProvisionPairedAsync(factory);
        using var client = TestUsers.CreateClient(factory);
        await SendAsync(client, device, events);

        await using var db = fixture.CreateContext();
        return await db.DeviceEvents.AsNoTracking()
            .Where(x => x.DeviceId == device.Provisioned.Device.Id)
            .Join(db.Compartments, e => e.CompartmentId, c => c.Id, (e, c) => new { e.EventId, e.OccurredAt, c.SlotNumber, e.EventType })
            .OrderBy(x => x.EventId)
            .Select(x => ValueTuple.Create(x.EventId, x.OccurredAt, x.SlotNumber, x.EventType))
            .ToListAsync();
    }

    private static dynamic Event(string eventId, int slot, string type, DateTimeOffset occurredAt) =>
        new { eventId, compartmentSlot = slot, eventType = type, occurredAt };

    private static HttpRequestMessage Request(ProvisioningResult device, object[] events)
    {
        var request = DeviceAuthenticationTests.DeviceRequest(device.Provisioned.Device.DeviceId, device.DeviceApiKey, Url);
        request.Content = JsonContent.Create(new { batchId = Guid.NewGuid().ToString(), events });
        return request;
    }

    private static async Task<List<ResultRow>> SendAsync(HttpClient client, ProvisioningResult device, object[] events)
    {
        using var response = await client.SendAsync(Request(device, events));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultsAsync(response);
    }

    private static async Task<List<ResultRow>> ReadResultsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Now, body.RootElement.GetProperty("serverTimeUtc").GetDateTimeOffset());
        return body.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => new ResultRow(
                r.GetProperty("eventId").GetString()!,
                r.GetProperty("status").GetString()!,
                r.GetProperty("code").GetString()))
            .ToList();
    }

    private async Task<ProvisioningResult> ProvisionPairedAsync(WebApplicationFactory<Program> factory)
    {
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        await using var db = fixture.CreateContext();
        var stored = await db.Devices.SingleAsync(x => x.Id == device.Provisioned.Device.Id);
        stored.OwnerUserId = owner.Id;
        await db.SaveChangesAsync();
        return device;
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }));
}
