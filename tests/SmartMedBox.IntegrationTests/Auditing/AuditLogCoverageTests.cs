using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Alerts;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.IntegrationTests.Devices;
using SmartMedBox.IntegrationTests.Prescriptions;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using static SmartMedBox.IntegrationTests.Relatives.RelativesTestSupport;

namespace SmartMedBox.IntegrationTests.Auditing;

/// <summary>P7-01: every important action leaves one audit row, and no row carries secrets or extra PII.</summary>
public sealed class AuditLogCoverageTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task FullFlow_AllImportantActions_AuditedWithoutSecretsOrPii()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var device = await DevicePairingApiTests.ProvisionAsync(factory);
        var deviceId = device.Provisioned.Device.Id;
        var owner = await TestUsers.SeedAsync(fixture);
        var relative = await TestUsers.SeedAsync(fixture);
        using var ownerClient = await LoginWithCsrfAsync(factory, owner);
        using var relativeClient = await LoginWithCsrfAsync(factory, relative);

        // Pair.
        Assert.Equal(HttpStatusCode.OK, (await DevicePairingApiTests.PairAsync(ownerClient, device.PairingCode.Code)).StatusCode);

        // Schedule create / update / delete (a second schedule stays for the dose below).
        var medicine = $"Thuoc-{Guid.NewGuid():N}";
        var scheduleId = await CreateScheduleAsync(ownerClient, medicine, slot: 1);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PutAsJsonAsync($"/api/schedules/{scheduleId}", ScheduleBody(medicine, 1, "08:00"))).StatusCode);
        var deletedScheduleId = await CreateScheduleAsync(ownerClient, medicine, slot: 2);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync($"/api/schedules/{deletedScheduleId}")).StatusCode);

        // Invitation create + accept, and a second invitation create + revoke.
        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(ownerClient, relative.Email)).StatusCode);
        var acceptToken = ExtractToken(factory.Emails.SingleTo(relative.Email).Body);
        using var accepted = await AcceptAsync(relativeClient, acceptToken);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var relationshipId = (await ReadJsonAsync(accepted)).GetProperty("relationshipId").GetGuid();
        var pendingEmail = $"pending-{Guid.NewGuid():N}@example.com";
        using var pending = await InviteAsync(ownerClient, pendingEmail);
        var pendingToken = ExtractToken(factory.Emails.SingleTo(pendingEmail).Body);
        var pendingId = (await ReadJsonAsync(pending)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.PostAsync($"/api/relatives/invitations/{pendingId}/revoke", null)).StatusCode);

        // Prescription upload + delete.
        var fileName = $"don-{Guid.NewGuid():N}.jpg";
        var file = new ByteArrayContent(PrescriptionTestSupport.Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var uploaded = await ownerClient.PostAsync("/api/prescriptions/images", new MultipartFormDataContent { { file, "file", fileName } });
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var imageId = (await ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync($"/api/prescriptions/images/{imageId}")).StatusCode);

        // Alert created by the evaluation job (device never online → Unknown).
        await SeedOverdueDoseAsync(deviceId, scheduleId);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(1, (await scope.ServiceProvider.GetRequiredService<AlertEvaluationService>().EvaluateAsync()).CreatedCount);
        }

        // Relative stops following.
        Assert.Equal(HttpStatusCode.NoContent, (await relativeClient.DeleteAsync($"/api/relatives/relationships/{relationshipId}")).StatusCode);

        await using var db = fixture.CreateContext();
        var rows = await db.AuditLogs.AsNoTracking().Where(x => x.DeviceId == deviceId).ToListAsync();
        Assert.Equal(
            new[]
            {
                AuditActions.DeviceProvisioned, AuditActions.DevicePaired,
                AuditActions.ScheduleCreated, AuditActions.ScheduleUpdated, AuditActions.ScheduleCreated, AuditActions.ScheduleDeleted,
                AuditActions.InvitationCreated, AuditActions.InvitationAccepted, AuditActions.InvitationCreated, AuditActions.InvitationRevoked,
                AuditActions.PrescriptionImageUploaded, AuditActions.PrescriptionImageDeleted,
                AuditActions.AlertCreated, AuditActions.RelationshipRevoked,
            }.Order(),
            rows.Select(x => x.Action).Order());

        var image = await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == imageId);
        var alert = await db.Alerts.AsNoTracking().SingleAsync();
        var forbidden = new List<string>
        {
            owner.Email, relative.Email, pendingEmail, owner.PasswordHash, TestUsers.Password,
            device.DeviceApiKey, device.Provisioned.Device.DeviceApiKeyHash, device.Provisioned.Device.DeviceId,
            device.PairingCode.Code, acceptToken, pendingToken,
            medicine, fileName, image.StorageKey, alert.Message,
        };
        forbidden.AddRange(await db.DevicePairingCodes.Where(x => x.DeviceId == deviceId).Select(x => x.CodeHash).ToListAsync());
        forbidden.AddRange(await db.RelativeInvitations.Where(x => x.DeviceId == deviceId).Select(x => x.TokenHash).ToListAsync());

        foreach (var row in rows)
        {
            var content = string.Join('|', row.Action, row.EntityType, row.EntityId, row.Details);
            Assert.All(forbidden, secret => Assert.DoesNotContain(secret, content, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("@", content);
        }
    }

    [Fact]
    public void Routes_NoEndpointExposesAuditLogs_AppendOnlyAtApiLevel()
    {
        using var factory = new TestApplicationFactory();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(x => x.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.NotEmpty(routes);
        Assert.DoesNotContain(routes, x => x.Contains("audit", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Guid> CreateScheduleAsync(HttpClient client, string medicine, int slot)
    {
        using var response = await client.PostAsJsonAsync("/api/schedules", ScheduleBody(medicine, slot, "07:00"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static JsonObject ScheduleBody(string medicine, int slot, string time) => new()
    {
        ["compartmentSlot"] = slot,
        ["timeOfDay"] = time,
        ["daysOfWeek"] = new JsonArray(1, 2, 3, 4, 5, 6, 7),
        ["startDate"] = "2026-10-01",
        ["gracePeriodMinutes"] = 15,
        ["timeZoneId"] = "Asia/Ho_Chi_Minh",
        ["items"] = new JsonArray(new JsonObject { ["medicineName"] = medicine, ["quantity"] = 1, ["unit"] = "viên", ["instructions"] = "" }),
    };

    private async Task SeedOverdueDoseAsync(Guid deviceId, Guid scheduleId)
    {
        await using var db = fixture.CreateContext();
        var compartmentId = await db.Compartments.Where(x => x.DeviceId == deviceId && x.SlotNumber == 1).Select(x => x.Id).SingleAsync();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(-5);
        db.DoseInstances.Add(new DoseInstance
        {
            Id = Guid.NewGuid(), ScheduleId = scheduleId, DeviceId = deviceId, CompartmentId = compartmentId,
            ScheduledAt = deadline.AddMinutes(-15), GracePeriodMinutes = 15, DeadlineAt = deadline, Status = DoseStatus.Pending,
        });
        await db.SaveChangesAsync();
    }
}
