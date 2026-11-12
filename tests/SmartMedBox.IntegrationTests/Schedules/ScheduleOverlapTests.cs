using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Schedules;

/// <summary>D-23 (API_CONTRACT §1.7): lịch chồng lấn cùng ngăn → 409 SCHEDULE_OVERLAP.</summary>
public sealed class ScheduleOverlapTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>, IDisposable
{
    private readonly TestApplicationFactory factory = new(fixture.ConnectionString);

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Create_ExactDuplicate_ReturnsOverlapWithoutWrites()
    {
        var (deviceId, client) = await SetupAsync();
        await CreateAsync(client, Body());

        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body()));

        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.MedicationSchedules.CountAsync(x => x.DeviceId == deviceId));
        Assert.Equal(1, await db.Devices.Where(x => x.Id == deviceId).Select(x => x.ScheduleVersion).SingleAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.DeviceId == deviceId));
    }

    [Theory]
    [InlineData("07:10")]
    [InlineData("06:50")]
    public async Task Create_PartialWindowOverlap_ReturnsOverlap(string time)
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: time)));
    }

    [Fact]
    public async Task Create_TouchingBoundary_IsInclusiveOverlap()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        // 07:00 + 15 phút = 07:15 → chạm biên vẫn là chồng lấn; 07:16 thì không.
        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: "07:15")));
        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: "06:45")));
        await CreateAsync(client, Body(time: "07:16"));
    }

    [Fact]
    public async Task Create_SameWindowDifferentSlot_ReturnsCreated()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        await CreateAsync(client, Body(slot: 2));
    }

    [Fact]
    public async Task Create_DisjointDays_ReturnsCreated()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        await CreateAsync(client, Body(days: [1, 3, 5, 7]));
    }

    [Fact]
    public async Task Create_DisjointDateRanges_ReturnsCreated()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        await CreateAsync(client, Body(start: "2027-01-01", end: null));
    }

    [Fact]
    public async Task Create_OpenEndedExisting_OverlapsFarFutureRange()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body(end: null));

        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(start: "2030-01-01", end: "2030-02-01")));
        // Kết thúc trước khi lịch mở bắt đầu → không giao.
        await CreateAsync(client, Body(start: "2026-01-01", end: "2026-09-30"));
    }

    [Fact]
    public async Task Create_WindowCrossingMidnight_OverlapsNextDayOnly()
    {
        var (_, client) = await SetupAsync();
        // Thứ Hai 23:50 + 30 phút → tràn sang 00:20 thứ Ba.
        await CreateAsync(client, Body(time: "23:50", days: [1], grace: 30));

        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: "00:10", days: [2])));
        await CreateAsync(client, Body(time: "00:10", days: [1]));
        await CreateAsync(client, Body(time: "00:21", days: [2]));
    }

    [Fact]
    public async Task Create_WindowCrossingMidnightPastEndDate_SpillsIntoNextDay()
    {
        var (_, client) = await SetupAsync();
        // 2026-10-05 là thứ Hai, ngày cuối của lịch; cửa sổ tràn sang thứ Ba 2026-10-06 nơi lịch mới bắt đầu.
        await CreateAsync(client, Body(time: "23:50", days: [1], grace: 30, start: "2026-10-01", end: "2026-10-05"));

        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: "00:10", days: [2], start: "2026-10-06", end: null)));
        await CreateAsync(client, Body(time: "00:10", days: [2], start: "2026-10-07", end: null));
    }

    [Fact]
    public async Task Create_DifferentTimeZoneSameUtcWindow_ReturnsOverlap()
    {
        var (_, client) = await SetupAsync();
        await CreateAsync(client, Body());

        // 09:05 Tokyo (UTC+9) = 07:05 Hồ Chí Minh (UTC+7).
        await AssertOverlapAsync(await client.PostAsJsonAsync("/api/schedules", Body(time: "09:05", timeZoneId: "Asia/Tokyo")));
        await CreateAsync(client, Body(time: "07:05", timeZoneId: "Asia/Tokyo"));
    }

    [Fact]
    public async Task Update_MovingIntoOverlap_ReturnsOverlapAndKeepsRow()
    {
        var (deviceId, client) = await SetupAsync();
        await CreateAsync(client, Body());
        var noon = await CreateAsync(client, Body(time: "12:00"));

        await AssertOverlapAsync(await client.PutAsJsonAsync($"/api/schedules/{noon}", Body(time: "07:05")));

        await using var db = fixture.CreateContext();
        Assert.Equal(new TimeOnly(12, 0), await db.MedicationSchedules.Where(x => x.Id == noon).Select(x => x.TimeOfDay).SingleAsync());
        Assert.Equal(2, await db.Devices.Where(x => x.Id == deviceId).Select(x => x.ScheduleVersion).SingleAsync());
        Assert.Equal(3, await db.ScheduleDays.CountAsync(x => x.ScheduleId == noon));
    }

    [Fact]
    public async Task Update_OverlappingOnlyItself_ReturnsOk()
    {
        var (_, client) = await SetupAsync();
        var id = await CreateAsync(client, Body());

        using var response = await client.PutAsJsonAsync($"/api/schedules/{id}", Body(time: "07:05"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_SoftDeletedDuplicate_ReturnsCreated()
    {
        var (_, client) = await SetupAsync();
        var id = await CreateAsync(client, Body());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/schedules/{id}")).StatusCode);

        await CreateAsync(client, Body());
    }

    [Fact]
    public async Task Create_ConcurrentOverlapping_ExactlyOneCreated()
    {
        var (deviceId, client) = await SetupAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync("/api/schedules", Body())));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(x => x.StatusCode != HttpStatusCode.Created), x => Assert.Equal(HttpStatusCode.Conflict, x.StatusCode));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.MedicationSchedules.CountAsync(x => x.DeviceId == deviceId));
        Assert.Equal(1, await db.Devices.Where(x => x.Id == deviceId).Select(x => x.ScheduleVersion).SingleAsync());
    }

    private static JsonObject Body(
        string time = "07:00",
        int slot = 1,
        int[]? days = null,
        int grace = 15,
        string start = "2026-10-01",
        string? end = "2026-12-31",
        string timeZoneId = "Asia/Ho_Chi_Minh") => new()
    {
        ["compartmentSlot"] = slot,
        ["timeOfDay"] = time,
        ["daysOfWeek"] = new JsonArray((days ?? [2, 4, 6]).Select(x => (JsonNode)x).ToArray()),
        ["startDate"] = start,
        ["endDate"] = end,
        ["gracePeriodMinutes"] = grace,
        ["timeZoneId"] = timeZoneId,
        ["items"] = new JsonArray(new JsonObject { ["medicineName"] = "Vitamin C", ["quantity"] = 1, ["unit"] = "viên" }),
    };

    private static async Task<Guid> CreateAsync(HttpClient client, JsonObject body)
    {
        using var response = await client.PostAsJsonAsync("/api/schedules", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task AssertOverlapAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("SCHEDULE_OVERLAP", json.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
        }
    }

    private async Task<(Guid DeviceId, HttpClient Client)> SetupAsync()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        Guid deviceId;
        await using (var db = fixture.CreateContext())
        {
            var provisioned = await new DeviceProvisioningService(db).ProvisionAsync(
                $"esp32-{Guid.NewGuid():N}",
                $"SN-{Guid.NewGuid():N}",
                Guid.NewGuid().ToString("N"));
            provisioned.Device.OwnerUserId = owner.Id;
            await db.SaveChangesAsync();
            deviceId = provisioned.Device.Id;
        }

        var client = await TestUsers.LoginAsync(factory, owner);
        using var csrf = JsonDocument.Parse(await (await client.GetAsync("/api/test-probe/csrf")).Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        return (deviceId, client);
    }
}
