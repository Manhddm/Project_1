using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Enums;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;
using static SmartMedBox.IntegrationTests.Dashboard.DashboardTestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Dashboard;

public sealed class AlertsApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task List_Relative_SeesOnlyOwnRecipientAlertsWithoutOtherEmails()
    {
        var (setup, shared, ownerOnly, dose) = await SeedAlertsAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);

        // userId do client gửi bị bỏ qua: người gọi luôn lấy từ phiên.
        using var response = await relative.GetAsync($"/api/alerts?deviceId={setup.DeviceId}&userId={setup.Owner.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(setup.OtherRelative.Email, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(setup.Owner.Email, raw, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("unreadCount").GetInt32());
        var alert = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(shared, alert.GetProperty("id").GetGuid());
        Assert.Equal(dose, alert.GetProperty("doseInstanceId").GetGuid());
        Assert.Equal("UnconfirmedOffline", alert.GetProperty("alertType").GetString());
        Assert.Equal("Không thể xác nhận do mất kết nối.", alert.GetProperty("message").GetString());
        Assert.Equal(Now.AddMinutes(-10), alert.GetProperty("createdAt").GetDateTimeOffset());
        // [TBD-D24] resolvedAt chưa có quy tắc set.
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, alert.GetProperty("readAt").ValueKind);

        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var ownerBody = await GetJsonAsync(owner, $"/api/alerts?deviceId={setup.DeviceId}");
        Assert.Equal(2, ownerBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            [shared, ownerOnly],
            ownerBody.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task List_UnreadOnly_UsesCallerReadAtOnly()
    {
        var (setup, shared, _, _) = await SeedAlertsAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);
        using (var read = await relative.PostAsync($"/api/alerts/{shared}/read", null))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using (var unread = await GetJsonAsync(relative, $"/api/alerts?deviceId={setup.DeviceId}&unreadOnly=true"))
        {
            Assert.Empty(unread.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(0, unread.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var all = await GetJsonAsync(relative, $"/api/alerts?deviceId={setup.DeviceId}"))
        {
            var alert = Assert.Single(all.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(Now, alert.GetProperty("readAt").GetDateTimeOffset());
        }

        using var other = await LoginWithCsrfAsync(factory, setup.OtherRelative);
        using var otherBody = await GetJsonAsync(other, $"/api/alerts?deviceId={setup.DeviceId}&unreadOnly=true");
        Assert.Equal(shared, Assert.Single(otherBody.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(1, otherBody.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task MarkRead_RepeatedCall_KeepsFirstReadAtForCallerOnly()
    {
        var (setup, shared, _, _) = await SeedAlertsAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);

        Assert.Equal(Now, await MarkReadAsync(relative, shared));
        _clock.UtcNow = Now.AddMinutes(5);
        Assert.Equal(Now, await MarkReadAsync(relative, shared));

        await using var db = fixture.CreateContext();
        var recipients = await db.AlertRecipients.AsNoTracking().Where(x => x.AlertId == shared).ToListAsync();
        Assert.Equal(Now, recipients.Single(x => x.RecipientUserId == setup.Relative.Id).ReadAt);
        Assert.All(recipients.Where(x => x.RecipientUserId != setup.Relative.Id), x => Assert.Null(x.ReadAt));
    }

    [Fact]
    public async Task MarkRead_NotRecipientOrMissing_ReturnsAlertNotFound()
    {
        var (setup, shared, ownerOnly, _) = await SeedAlertsAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);
        using var stranger = await LoginWithCsrfAsync(factory, setup.Stranger);

        await AssertCodeAsync(await relative.PostAsync($"/api/alerts/{ownerOnly}/read", null), HttpStatusCode.NotFound, "ALERT_NOT_FOUND");
        await AssertCodeAsync(await stranger.PostAsync($"/api/alerts/{shared}/read", null), HttpStatusCode.NotFound, "ALERT_NOT_FOUND");
        await AssertCodeAsync(await relative.PostAsync($"/api/alerts/{Guid.NewGuid()}/read", null), HttpStatusCode.NotFound, "ALERT_NOT_FOUND");

        await using var db = fixture.CreateContext();
        Assert.False(await db.AlertRecipients.AnyAsync(x => (x.AlertId == shared || x.AlertId == ownerOnly) && x.ReadAt != null));
    }

    [Fact]
    public async Task MarkRead_RecipientRevokedAfterAlertCreated_ReturnsAlertNotFoundAndKeepsReadAtNull()
    {
        var (setup, alert) = await SeedRevokedRecipientAlertAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var revoked = await LoginWithCsrfAsync(factory, setup.RevokedRelative);

        await AssertCodeAsync(await revoked.PostAsync($"/api/alerts/{alert}/read", null), HttpStatusCode.NotFound, "ALERT_NOT_FOUND");

        await using var db = fixture.CreateContext();
        Assert.Null(await db.AlertRecipients.Where(x => x.AlertId == alert && x.RecipientUserId == setup.RevokedRelative.Id).Select(x => x.ReadAt).SingleAsync());
    }

    [Fact]
    public async Task List_RecipientRevokedAfterAlertCreated_ReturnsForbidden()
    {
        var (setup, _) = await SeedRevokedRecipientAlertAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var revoked = await LoginWithCsrfAsync(factory, setup.RevokedRelative);

        await AssertCodeAsync(await revoked.GetAsync($"/api/alerts?deviceId={setup.DeviceId}"), HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task MarkRead_WithoutCsrfToken_ReturnsCsrfInvalid()
    {
        var (setup, shared, _, _) = await SeedAlertsAsync();
        using var factory = CreateFactory(fixture, _clock);
        using var relative = await TestUsers.LoginAsync(factory, setup.Relative);

        await AssertCodeAsync(await relative.PostAsync($"/api/alerts/{shared}/read", null), HttpStatusCode.BadRequest, "CSRF_INVALID");

        await using var db = fixture.CreateContext();
        Assert.False(await db.AlertRecipients.AnyAsync(x => x.AlertId == shared && x.ReadAt != null));
    }

    /// <summary>Alert chung (chủ + hai người thân) và alert chỉ gửi chủ (cũ hơn).</summary>
    private async Task<(DashboardSetup Setup, Guid Shared, Guid OwnerOnly, Guid Dose)> SeedAlertsAsync()
    {
        var setup = await SeedAsync(fixture);
        var schedule = await SeedScheduleAsync(fixture, setup.DeviceId, "Asia/Ho_Chi_Minh");
        var dose = await SeedDoseAsync(fixture, schedule, Now.AddHours(-1), DoseStatus.Unknown);
        var olderDose = await SeedDoseAsync(fixture, schedule, Now.AddHours(-2), DoseStatus.Unknown);
        var shared = await SeedAlertAsync(fixture, dose, Now.AddMinutes(-10), setup.Owner.Id, setup.Relative.Id, setup.OtherRelative.Id);
        var ownerOnly = await SeedAlertAsync(fixture, olderDose, Now.AddMinutes(-70), setup.Owner.Id);
        return (setup, shared, ownerOnly, dose);
    }

    /// <summary>Alert whose recipients include the relative SeedAsync stores as Revoked (i.e. revoked after the alert was created).</summary>
    private async Task<(DashboardSetup Setup, Guid Alert)> SeedRevokedRecipientAlertAsync()
    {
        var setup = await SeedAsync(fixture);
        var schedule = await SeedScheduleAsync(fixture, setup.DeviceId, "Asia/Ho_Chi_Minh");
        var dose = await SeedDoseAsync(fixture, schedule, Now.AddHours(-1), DoseStatus.Unknown);
        return (setup, await SeedAlertAsync(fixture, dose, Now.AddMinutes(-10), setup.Owner.Id, setup.RevokedRelative.Id));
    }

    private static async Task<DateTimeOffset> MarkReadAsync(HttpClient client, Guid alertId)
    {
        using var response = await client.PostAsync($"/api/alerts/{alertId}/read", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("readAt").GetDateTimeOffset();
    }
}
