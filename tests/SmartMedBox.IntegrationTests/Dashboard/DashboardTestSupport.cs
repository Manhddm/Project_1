using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Relatives;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Dashboard;

internal sealed record DashboardSetup(
    Guid DeviceId,
    string SerialNumber,
    ApplicationUser Owner,
    ApplicationUser Relative,
    ApplicationUser OtherRelative,
    ApplicationUser RevokedRelative,
    ApplicationUser Stranger);

internal static class DashboardTestSupport
{
    // Cookie phiên so với giờ thật: FakeClock phải bám giờ hiện tại, không dùng ngày cố định.
    public static readonly DateTimeOffset Now =
        new(DateTimeOffset.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    public static WebApplicationFactory<Program> CreateFactory(SqlServerDatabaseFixture fixture, FakeClock clock) =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(clock);
            }));

    /// <summary>Hộp đã ghép (LastSeenAt = Now), hai người thân Active, một người thân đã thu hồi, một người ngoài.</summary>
    public static async Task<DashboardSetup> SeedAsync(SqlServerDatabaseFixture fixture)
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var relative = await TestUsers.SeedAsync(fixture);
        var otherRelative = await TestUsers.SeedAsync(fixture);
        var revoked = await TestUsers.SeedAsync(fixture);
        var stranger = await TestUsers.SeedAsync(fixture);

        Guid deviceId;
        string serial = $"SN-{Guid.NewGuid():N}";
        await using (var db = fixture.CreateContext())
        {
            var provisioned = await new DeviceProvisioningService(db).ProvisionAsync(
                $"esp32-{Guid.NewGuid():N}", serial, Guid.NewGuid().ToString("N"));
            provisioned.Device.OwnerUserId = owner.Id;
            provisioned.Device.LastSeenAt = Now;
            await db.SaveChangesAsync();
            deviceId = provisioned.Device.Id;
        }

        await RelativesTestSupport.SeedRelationshipAsync(fixture, deviceId, relative.Id);
        await RelativesTestSupport.SeedRelationshipAsync(fixture, deviceId, otherRelative.Id);
        await RelativesTestSupport.SeedRelationshipAsync(fixture, deviceId, revoked.Id, RelationshipStatus.Revoked);
        return new DashboardSetup(deviceId, serial, owner, relative, otherRelative, revoked, stranger);
    }

    public static async Task<Guid> SeedScheduleAsync(SqlServerDatabaseFixture fixture, Guid deviceId, string timeZoneId, int slot = 1)
    {
        await using var db = fixture.CreateContext();
        var compartmentId = await db.Compartments
            .Where(x => x.DeviceId == deviceId && x.SlotNumber == slot)
            .Select(x => x.Id)
            .SingleAsync();
        var schedule = new MedicationSchedule
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            CompartmentId = compartmentId,
            TimeOfDay = new TimeOnly(7, 0),
            StartDate = new DateOnly(2026, 1, 1),
            GracePeriodMinutes = 15,
            TimeZoneId = timeZoneId,
        };
        db.MedicationSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    public static async Task<Guid> SeedDoseAsync(
        SqlServerDatabaseFixture fixture,
        Guid scheduleId,
        DateTimeOffset scheduledAt,
        DoseStatus status = DoseStatus.Pending,
        string medicineName = "Vitamin C")
    {
        await using var db = fixture.CreateContext();
        var schedule = await db.MedicationSchedules.AsNoTracking().SingleAsync(x => x.Id == scheduleId);
        var dose = new DoseInstance
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            DeviceId = schedule.DeviceId,
            CompartmentId = schedule.CompartmentId,
            ScheduledAt = scheduledAt,
            GracePeriodMinutes = 15,
            DeadlineAt = scheduledAt.AddMinutes(15),
            Status = status,
            ConfirmedAt = status == DoseStatus.Confirmed ? scheduledAt.AddMinutes(3) : null,
        };
        db.DoseInstances.Add(dose);
        db.DoseItemSnapshots.Add(new DoseItemSnapshot
        {
            Id = Guid.NewGuid(),
            DoseInstanceId = dose.Id,
            MedicineName = medicineName,
            Quantity = 1,
            Unit = "viên",
            Instructions = "Uống sau ăn",
        });
        await db.SaveChangesAsync();
        return dose.Id;
    }

    public static async Task<Guid> SeedAlertAsync(
        SqlServerDatabaseFixture fixture,
        Guid doseId,
        DateTimeOffset createdAt,
        params Guid[] recipientUserIds)
    {
        await using var db = fixture.CreateContext();
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            DoseInstanceId = doseId,
            AlertType = AlertType.UnconfirmedOffline,
            Message = "Không thể xác nhận do mất kết nối.",
            CreatedAt = createdAt,
        };
        db.Alerts.Add(alert);
        db.AlertRecipients.AddRange(recipientUserIds.Select(userId => new AlertRecipient
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            RecipientUserId = userId,
            Channel = "Email",
        }));
        await db.SaveChangesAsync();
        return alert.Id;
    }

    public static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }
    }
}
