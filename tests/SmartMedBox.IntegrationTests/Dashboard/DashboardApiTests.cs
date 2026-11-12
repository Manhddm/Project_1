using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartMedBox.Domain.Enums;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;
using static SmartMedBox.IntegrationTests.Dashboard.DashboardTestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Dashboard;

public sealed class DashboardApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string HoChiMinh = "Asia/Ho_Chi_Minh";
    // UTC+14 và UTC-11 luôn lệch nhau đúng một ngày lịch: kiểm thử "hôm nay" theo timezone của từng lịch mà không cần ngày cố định.
    private const string Kiritimati = "Pacific/Kiritimati";
    private const string PagoPago = "Pacific/Pago_Pago";

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task ReadEndpoints_AuthorizationMatrix_OwnerAndActiveRelativeOnly()
    {
        var setup = await SeedAsync(fixture);
        using var factory = CreateFactory(fixture, _clock);
        var urls = ReadUrls(setup.DeviceId);

        foreach (var user in new[] { setup.Owner, setup.Relative })
        {
            using var client = await LoginWithCsrfAsync(factory, user);
            foreach (var url in urls)
            {
                using var response = await client.GetAsync(url);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }

        foreach (var user in new[] { setup.RevokedRelative, setup.Stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, user);
            foreach (var url in urls)
            {
                await AssertCodeAsync(await client.GetAsync(url), HttpStatusCode.Forbidden, "FORBIDDEN");
            }
        }

        // Hộp không tồn tại trả cùng 403 để không lộ tồn tại.
        using (var owner = await LoginWithCsrfAsync(factory, setup.Owner))
        {
            foreach (var url in ReadUrls(Guid.NewGuid()))
            {
                await AssertCodeAsync(await owner.GetAsync(url), HttpStatusCode.Forbidden, "FORBIDDEN");
            }
        }

        using var anonymous = TestUsers.CreateClient(factory);
        foreach (var url in urls)
        {
            using var response = await anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var anonymousPost = await anonymous.PostAsync($"/api/alerts/{Guid.NewGuid()}/read", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousPost.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoints_ClientSuppliedUserId_IsIgnored()
    {
        var setup = await SeedAsync(fixture);
        using var factory = CreateFactory(fixture, _clock);
        using var stranger = await LoginWithCsrfAsync(factory, setup.Stranger);

        foreach (var url in ReadUrls(setup.DeviceId))
        {
            await AssertCodeAsync(
                await stranger.GetAsync($"{url}&userId={setup.Owner.Id}"),
                HttpStatusCode.Forbidden,
                "FORBIDDEN");
        }
    }

    [Fact]
    public async Task Dashboard_DosesInDifferentTimeZones_TodayUsesEachScheduleTimeZoneAndStoredStatus()
    {
        var setup = await SeedAsync(fixture);
        var kiriSchedule = await SeedScheduleAsync(fixture, setup.DeviceId, Kiritimati, slot: 1);
        var pagoSchedule = await SeedScheduleAsync(fixture, setup.DeviceId, PagoPago, slot: 3);
        var kiriToday = LocalDate(Now, Kiritimati);
        var pagoToday = LocalDate(Now, PagoPago);
        Assert.NotEqual(kiriToday, pagoToday);

        var expectedStatuses = new Dictionary<Guid, string>
        {
            [await SeedDoseAsync(fixture, kiriSchedule, At(Kiritimati, kiriToday, 6), DoseStatus.Pending)] = "Pending",
            [await SeedDoseAsync(fixture, kiriSchedule, At(Kiritimati, kiriToday, 8), DoseStatus.Confirmed)] = "Confirmed",
            [await SeedDoseAsync(fixture, kiriSchedule, At(Kiritimati, kiriToday, 10), DoseStatus.Overdue)] = "Overdue",
            [await SeedDoseAsync(fixture, kiriSchedule, At(Kiritimati, kiriToday, 12), DoseStatus.Unknown)] = "Unknown",
            [await SeedDoseAsync(fixture, pagoSchedule, At(PagoPago, pagoToday, 12), medicineName: "Canxi")] = "Pending",
        };
        // Hôm qua theo Kiritimati và "ngày mai" theo Pago Pago (cùng ngày lịch với hôm nay của Kiritimati): không thuộc hôm nay.
        await SeedDoseAsync(fixture, kiriSchedule, At(Kiritimati, kiriToday.AddDays(-1), 12));
        await SeedDoseAsync(fixture, pagoSchedule, At(PagoPago, kiriToday, 12));

        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);
        using var response = await relative.GetAsync($"/api/dashboard?deviceId={setup.DeviceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("taken", raw, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(raw);

        var today = body.RootElement.GetProperty("today");
        Assert.Equal(HoChiMinh, today.GetProperty("timeZoneId").GetString());
        Assert.Equal(LocalDate(Now, HoChiMinh).ToString("yyyy-MM-dd"), today.GetProperty("date").GetString());

        var doses = today.GetProperty("doses").EnumerateArray().ToList();
        Assert.Equal(expectedStatuses.Count, doses.Count);
        foreach (var dose in doses)
        {
            var id = dose.GetProperty("id").GetGuid();
            Assert.Equal(expectedStatuses[id], dose.GetProperty("status").GetString());
            var zone = dose.GetProperty("timeZoneId").GetString();
            var expectedDate = zone == Kiritimati ? kiriToday : pagoToday;
            Assert.Equal(expectedDate.ToString("yyyy-MM-dd"), dose.GetProperty("localDate").GetString());
            Assert.Equal(15, dose.GetProperty("gracePeriodMinutes").GetInt32());
            Assert.Equal(zone == Kiritimati ? 1 : 3, dose.GetProperty("compartmentSlot").GetInt32());
            Assert.Equal(zone == Kiritimati ? "Vitamin C" : "Canxi", dose.GetProperty("items")[0].GetProperty("medicineName").GetString());
        }
    }

    [Fact]
    public async Task Dashboard_Relative_ShowsNextPendingDoseCallerAlertsAndNullIsOnline()
    {
        var setup = await SeedAsync(fixture);
        var schedule = await SeedScheduleAsync(fixture, setup.DeviceId, HoChiMinh);
        var overdue = await SeedDoseAsync(fixture, schedule, Now.AddHours(-2), DoseStatus.Overdue);
        var unknown = await SeedDoseAsync(fixture, schedule, Now.AddHours(-1), DoseStatus.Unknown);
        var next = await SeedDoseAsync(fixture, schedule, Now.AddHours(1));
        await SeedDoseAsync(fixture, schedule, Now.AddHours(3));
        var shared = await SeedAlertAsync(fixture, unknown, Now.AddMinutes(-30), setup.Owner.Id, setup.Relative.Id, setup.OtherRelative.Id);
        await SeedAlertAsync(fixture, overdue, Now.AddMinutes(-90), setup.Owner.Id);

        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);
        using (var body = await GetJsonAsync(relative, $"/api/dashboard?deviceId={setup.DeviceId}"))
        {
            var device = body.RootElement.GetProperty("device");
            Assert.Equal(setup.DeviceId, device.GetProperty("id").GetGuid());
            Assert.Equal(setup.SerialNumber, device.GetProperty("serialNumber").GetString());
            // [TBD-D12] ngưỡng online chưa chốt: không được tự kết luận online/offline.
            Assert.Equal(JsonValueKind.Null, device.GetProperty("isOnline").ValueKind);
            Assert.Equal(Now, device.GetProperty("lastSeenAt").GetDateTimeOffset());
            Assert.Equal(0, device.GetProperty("scheduleVersion").GetInt32());

            Assert.Equal(next, body.RootElement.GetProperty("nextDose").GetProperty("id").GetGuid());

            var alerts = body.RootElement.GetProperty("alerts");
            Assert.Equal(1, alerts.GetProperty("unreadCount").GetInt32());
            var latest = Assert.Single(alerts.GetProperty("latest").EnumerateArray());
            Assert.Equal(shared, latest.GetProperty("id").GetGuid());
            Assert.Equal("UnconfirmedOffline", latest.GetProperty("alertType").GetString());
            Assert.Equal(JsonValueKind.Null, latest.GetProperty("readAt").ValueKind);
        }

        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var ownerBody = await GetJsonAsync(owner, $"/api/dashboard?deviceId={setup.DeviceId}");
        Assert.Equal(2, ownerBody.RootElement.GetProperty("alerts").GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task DoseHistory_RangeStatusAndPaging_FollowsContract()
    {
        var setup = await SeedAsync(fixture);
        var schedule = await SeedScheduleAsync(fixture, setup.DeviceId, HoChiMinh);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var day = (int offset) => new DateTimeOffset(today.AddDays(offset).ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero);
        await SeedDoseAsync(fixture, schedule, day(-3), DoseStatus.Confirmed);
        var overdue = await SeedDoseAsync(fixture, schedule, day(-2), DoseStatus.Overdue);
        var unknown = await SeedDoseAsync(fixture, schedule, day(-1), DoseStatus.Unknown);
        await SeedDoseAsync(fixture, schedule, day(0));

        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);
        var url = $"/api/dose-history?deviceId={setup.DeviceId}";

        using (var range = await GetJsonAsync(relative, $"{url}&from={today.AddDays(-2):yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}"))
        {
            Assert.Equal(2, range.RootElement.GetProperty("total").GetInt32());
            var items = range.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal([unknown, overdue], items.Select(x => x.GetProperty("id").GetGuid()));
            Assert.Equal(["Unknown", "Overdue"], items.Select(x => x.GetProperty("status").GetString()));
        }

        using (var paged = await GetJsonAsync(relative, $"{url}&page=2&pageSize=1"))
        {
            Assert.Equal(4, paged.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(2, paged.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(1, paged.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(unknown, Assert.Single(paged.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var filtered = await GetJsonAsync(relative, $"{url}&status=Overdue"))
        {
            Assert.Equal(1, filtered.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(overdue, Assert.Single(filtered.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        await AssertCodeAsync(
            await relative.GetAsync($"{url}&from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}"),
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR");
    }

    [Fact]
    public async Task DoseHistory_ScheduleEditedAfterGeneration_ReturnsSnapshotItems()
    {
        var setup = await SeedAsync(fixture);
        using var factory = CreateFactory(fixture, _clock);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var created = await owner.PostAsJsonAsync("/api/schedules", ScheduleBody("Vitamin C"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var scheduleId = createdBody.RootElement.GetProperty("id").GetGuid();
        var doseId = await SeedDoseAsync(fixture, scheduleId, Now.AddDays(-1), DoseStatus.Confirmed, "Vitamin C");

        using (var updated = await owner.PutAsJsonAsync($"/api/schedules/{scheduleId}", ScheduleBody("Canxi")))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        using var history = await GetJsonAsync(owner, $"/api/dose-history?deviceId={setup.DeviceId}");
        var dose = Assert.Single(history.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(doseId, dose.GetProperty("id").GetGuid());
        Assert.Equal("Confirmed", dose.GetProperty("status").GetString());
        Assert.Equal(Now.AddDays(-1).AddMinutes(3), dose.GetProperty("confirmedAt").GetDateTimeOffset());
        var item = Assert.Single(dose.GetProperty("items").EnumerateArray());
        Assert.Equal("Vitamin C", item.GetProperty("medicineName").GetString());
    }

    private static string[] ReadUrls(Guid deviceId) =>
    [
        $"/api/dashboard?deviceId={deviceId}",
        $"/api/dose-history?deviceId={deviceId}",
        $"/api/alerts?deviceId={deviceId}",
    ];

    // Lịch luôn thuộc hộp của chủ đang đăng nhập; request không mang deviceId.
    private static JsonObject ScheduleBody(string medicineName) => new()
    {
        ["compartmentSlot"] = 1,
        ["timeOfDay"] = "07:00",
        ["daysOfWeek"] = new JsonArray(1, 2, 3, 4, 5, 6, 7),
        ["startDate"] = "2026-01-01",
        ["gracePeriodMinutes"] = 15,
        ["timeZoneId"] = HoChiMinh,
        ["items"] = new JsonArray(new JsonObject
        {
            ["medicineName"] = medicineName,
            ["quantity"] = 1,
            ["unit"] = "viên",
            ["instructions"] = "Uống sau ăn",
        }),
    };

    private static DateOnly LocalDate(DateTimeOffset instant, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);

    private static DateTimeOffset At(string timeZoneId, DateOnly date, int hour) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, 0)), TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)), TimeSpan.Zero);
}
