using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Schedules;

public sealed class SchedulesApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task Crud_Owner_CreatesReadsUpdatesSoftDeletesAndIncrementsVersion()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        using var created = await owner.PostAsJsonAsync("/api/schedules", Body());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(1, createdBody.RootElement.GetProperty("scheduleVersion").GetInt32());
        Assert.EndsWith($"/api/schedules/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using (var get = await ReadJsonAsync(await owner.GetAsync($"/api/schedules/{id}")))
        {
            var root = get.RootElement;
            Assert.Equal(setup.DeviceId, root.GetProperty("deviceId").GetGuid());
            Assert.Equal(1, root.GetProperty("compartmentSlot").GetInt32());
            Assert.Equal("Sáng", root.GetProperty("compartmentName").GetString());
            Assert.Equal("07:00", root.GetProperty("timeOfDay").GetString());
            Assert.Equal([2, 4, 6], root.GetProperty("daysOfWeek").EnumerateArray().Select(x => x.GetInt32()));
            Assert.Equal("2026-10-01", root.GetProperty("startDate").GetString());
            Assert.Equal("Asia/Ho_Chi_Minh", root.GetProperty("timeZoneId").GetString());
            Assert.Equal(2, root.GetProperty("items").GetArrayLength());
            Assert.Equal(1, root.GetProperty("version").GetInt32());
        }

        var update = Body();
        update["compartmentSlot"] = 3;
        update["timeOfDay"] = "20:30";
        update["daysOfWeek"] = new JsonArray(1);
        update["items"] = new JsonArray(Item("Canxi", 1.5m));
        using (var put = await owner.PutAsJsonAsync($"/api/schedules/{id}", update))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            using var putBody = await ReadJsonAsync(put);
            Assert.Equal(2, putBody.RootElement.GetProperty("scheduleVersion").GetInt32());
        }

        // PUT lặp lại không nhân đôi ngày/thuốc.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/schedules/{id}", update)).StatusCode);
        using (var list = await ReadJsonAsync(await owner.GetAsync($"/api/schedules?deviceId={setup.DeviceId}")))
        {
            var schedule = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(3, schedule.GetProperty("compartmentSlot").GetInt32());
            Assert.Equal("20:30", schedule.GetProperty("timeOfDay").GetString());
            Assert.Equal([1], schedule.GetProperty("daysOfWeek").EnumerateArray().Select(x => x.GetInt32()));
            var item = Assert.Single(schedule.GetProperty("items").EnumerateArray());
            Assert.Equal(1.5m, item.GetProperty("quantity").GetDecimal());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/schedules/{id}")).StatusCode);
        await AssertCodeAsync(await owner.GetAsync($"/api/schedules/{id}"), HttpStatusCode.NotFound, "SCHEDULE_NOT_FOUND");
        await AssertCodeAsync(await owner.DeleteAsync($"/api/schedules/{id}"), HttpStatusCode.NotFound, "SCHEDULE_NOT_FOUND");
        await AssertCodeAsync(await owner.PutAsJsonAsync($"/api/schedules/{id}", update), HttpStatusCode.NotFound, "SCHEDULE_NOT_FOUND");
        using (var list = await ReadJsonAsync(await owner.GetAsync($"/api/schedules?deviceId={setup.DeviceId}")))
        {
            Assert.Equal(0, list.RootElement.GetProperty("items").GetArrayLength());
        }

        await using var db = fixture.CreateContext();
        var row = await db.MedicationSchedules.SingleAsync(x => x.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal(1, await db.ScheduleItems.CountAsync(x => x.ScheduleId == id));
        Assert.Equal(4, await DeviceVersionAsync(setup.DeviceId));
        var actions = await db.AuditLogs
            .Where(x => x.EntityId == id.ToString())
            .OrderBy(x => x.OccurredAt)
            .Select(x => x.Action)
            .ToListAsync();
        Assert.Equal(
            [AuditActions.ScheduleCreated, AuditActions.ScheduleUpdated, AuditActions.ScheduleUpdated, AuditActions.ScheduleDeleted],
            actions);
        Assert.DoesNotContain(await db.AuditLogs.Where(x => x.EntityId == id.ToString()).Select(x => x.Details).ToListAsync(), d => d.Contains("Canxi"));
    }

    [Fact]
    public async Task Endpoints_Relative_ReadsButWritesForbidden()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var id = await CreateAsOwnerAsync(factory, setup);
        using var relative = await LoginWithCsrfAsync(factory, setup.Relative);

        Assert.Equal(HttpStatusCode.OK, (await relative.GetAsync($"/api/schedules?deviceId={setup.DeviceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await relative.GetAsync($"/api/schedules/{id}")).StatusCode);
        await AssertCodeAsync(await relative.PostAsJsonAsync("/api/schedules", Body()), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await relative.PutAsJsonAsync($"/api/schedules/{id}", Body()), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await relative.DeleteAsync($"/api/schedules/{id}"), HttpStatusCode.Forbidden, "FORBIDDEN");

        Assert.Equal(1, await DeviceVersionAsync(setup.DeviceId));
    }

    [Fact]
    public async Task Endpoints_Stranger_ForbiddenAndCreateWithoutDeviceNotPaired()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var id = await CreateAsOwnerAsync(factory, setup);
        using var stranger = await LoginWithCsrfAsync(factory, setup.Stranger);

        await AssertCodeAsync(await stranger.GetAsync($"/api/schedules?deviceId={setup.DeviceId}"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await stranger.GetAsync($"/api/schedules/{id}"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await stranger.PutAsJsonAsync($"/api/schedules/{id}", Body()), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await stranger.DeleteAsync($"/api/schedules/{id}"), HttpStatusCode.Forbidden, "FORBIDDEN");
        await AssertCodeAsync(await stranger.PostAsJsonAsync("/api/schedules", Body()), HttpStatusCode.Conflict, "DEVICE_NOT_PAIRED");

        using var anonymous = TestUsers.CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/schedules/{id}")).StatusCode);
        Assert.Equal(1, await DeviceVersionAsync(setup.DeviceId));
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("SE Asia Standard Time")]
    public async Task Create_InvalidTimeZone_ReturnsScheduleInvalidTimezone(string timeZoneId)
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        var body = Body();
        body["timeZoneId"] = timeZoneId;

        await AssertCodeAsync(await owner.PostAsJsonAsync("/api/schedules", body), HttpStatusCode.BadRequest, "SCHEDULE_INVALID_TIMEZONE");
        Assert.Equal(0, await DeviceVersionAsync(setup.DeviceId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Create_SlotOutOfRange_ReturnsCompartmentInvalid(int slot)
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        var body = Body();
        body["compartmentSlot"] = slot;

        await AssertCodeAsync(await owner.PostAsJsonAsync("/api/schedules", body), HttpStatusCode.BadRequest, "COMPARTMENT_INVALID");
    }

    [Fact]
    public async Task Create_SlotOnlyExistingOnOtherDevice_ReturnsCompartmentInvalid()
    {
        // Hộp của chủ chỉ có ngăn 1; hộp khác có đủ 3 ngăn — ngăn phải được tra theo hộp của chủ.
        var owner = await TestUsers.SeedAsync(fixture);
        var otherOwner = await TestUsers.SeedAsync(fixture);
        await ProvisionAsync(otherOwner.Id);
        await using (var db = fixture.CreateContext())
        {
            var device = new Device
            {
                Id = Guid.NewGuid(),
                DeviceId = $"esp32-{Guid.NewGuid():N}",
                SerialNumber = $"SN-{Guid.NewGuid():N}",
                DeviceApiKeyHash = Guid.NewGuid().ToString("N"),
                OwnerUserId = owner.Id,
            };
            db.Devices.Add(device);
            db.Compartments.Add(new Compartment { Id = Guid.NewGuid(), DeviceId = device.Id, SlotNumber = 1, Name = "Sáng", IsEnabled = true });
            await db.SaveChangesAsync();
        }

        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await LoginWithCsrfAsync(factory, owner);
        var body = Body();
        body["compartmentSlot"] = 2;

        await AssertCodeAsync(await client.PostAsJsonAsync("/api/schedules", body), HttpStatusCode.BadRequest, "COMPARTMENT_INVALID");
    }

    [Theory]
    [InlineData("timeOfDay", "\"24:00\"")]
    [InlineData("daysOfWeek", "[]")]
    [InlineData("daysOfWeek", "[0]")]
    [InlineData("daysOfWeek", "[8]")]
    [InlineData("daysOfWeek", "[2,2]")]
    [InlineData("endDate", "\"2026-09-30\"")]
    [InlineData("gracePeriodMinutes", "0")]
    [InlineData("gracePeriodMinutes", "121")]
    [InlineData("items", "[]")]
    [InlineData("items", "[{\"medicineName\":\"A\",\"quantity\":0,\"unit\":\"viên\"}]")]
    [InlineData("items", "[{\"medicineName\":\"A\",\"quantity\":1.234,\"unit\":\"viên\"}]")]
    public async Task Create_InvalidField_ReturnsValidationErrorWithoutCreating(string field, string json)
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        var body = Body();
        body[field] = JsonNode.Parse(json);

        using var response = await owner.PostAsJsonAsync("/api/schedules", body);

        await AssertCodeAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await using var db = fixture.CreateContext();
        Assert.False(await db.MedicationSchedules.AnyAsync(x => x.DeviceId == setup.DeviceId));
        Assert.Equal(0, await DeviceVersionAsync(setup.DeviceId));
    }

    [Fact]
    public async Task Update_ExistingDoseInstance_RemainsUnchanged()
    {
        var setup = await SeedAsync();
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var id = await CreateAsOwnerAsync(factory, setup);

        var doseId = Guid.NewGuid();
        var scheduledAt = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        await using (var db = fixture.CreateContext())
        {
            var schedule = await db.MedicationSchedules.SingleAsync(x => x.Id == id);
            db.DoseInstances.Add(new DoseInstance
            {
                Id = doseId,
                ScheduleId = id,
                DeviceId = setup.DeviceId,
                CompartmentId = schedule.CompartmentId,
                ScheduledAt = scheduledAt,
                GracePeriodMinutes = 15,
                DeadlineAt = scheduledAt.AddMinutes(15),
                Status = DoseStatus.Pending,
            });
            db.DoseItemSnapshots.Add(new DoseItemSnapshot
            {
                Id = Guid.NewGuid(),
                DoseInstanceId = doseId,
                MedicineName = "Vitamin C",
                Quantity = 1,
                Unit = "viên",
                Instructions = "Uống sau ăn",
            });
            await db.SaveChangesAsync();
        }

        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        var update = Body();
        update["compartmentSlot"] = 2;
        update["timeOfDay"] = "12:00";
        update["gracePeriodMinutes"] = 60;
        update["items"] = new JsonArray(Item("Canxi", 2));
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/schedules/{id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/schedules/{id}")).StatusCode);

        await using var verify = fixture.CreateContext();
        var dose = await verify.DoseInstances.SingleAsync(x => x.Id == doseId);
        var snapshot = await verify.DoseItemSnapshots.SingleAsync(x => x.DoseInstanceId == doseId);
        Assert.Equal(scheduledAt, dose.ScheduledAt);
        Assert.Equal(15, dose.GracePeriodMinutes);
        Assert.Equal(scheduledAt.AddMinutes(15), dose.DeadlineAt);
        Assert.Equal(DoseStatus.Pending, dose.Status);
        Assert.Equal(setup.CompartmentSlot1Id, dose.CompartmentId);
        Assert.Equal("Vitamin C", snapshot.MedicineName);
        Assert.Equal(1m, snapshot.Quantity);
    }

    private static JsonObject Body() => new()
    {
        ["compartmentSlot"] = 1,
        ["timeOfDay"] = "07:00",
        ["daysOfWeek"] = new JsonArray(2, 4, 6),
        ["startDate"] = "2026-10-01",
        ["endDate"] = "2026-12-31",
        ["gracePeriodMinutes"] = 15,
        ["timeZoneId"] = "Asia/Ho_Chi_Minh",
        ["items"] = new JsonArray(Item("Vitamin C", 1), Item("Canxi", 2)),
    };

    private static JsonObject Item(string name, decimal quantity) => new()
    {
        ["medicineName"] = name,
        ["quantity"] = quantity,
        ["unit"] = "viên",
        ["instructions"] = "Uống sau ăn",
    };

    private static async Task<HttpClient> LoginWithCsrfAsync(TestApplicationFactory factory, ApplicationUser user)
    {
        var client = await TestUsers.LoginAsync(factory, user);
        // Token antiforgery gắn với danh tính: phải lấy lại sau khi đăng nhập.
        using var csrf = await ReadJsonAsync(await client.GetAsync("/api/test-probe/csrf"));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        return client;
    }

    private static async Task<Guid> CreateAsOwnerAsync(TestApplicationFactory factory, Setup setup)
    {
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var created = await owner.PostAsJsonAsync("/api/schedules", Body());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = await ReadJsonAsync(created);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            using var body = await ReadJsonAsync(response);
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }
    }

    private async Task<int> DeviceVersionAsync(Guid deviceId)
    {
        await using var db = fixture.CreateContext();
        return await db.Devices.Where(x => x.Id == deviceId).Select(x => x.ScheduleVersion).SingleAsync();
    }

    private async Task<(Guid DeviceId, Guid Slot1Id)> ProvisionAsync(Guid ownerUserId)
    {
        await using var db = fixture.CreateContext();
        var provisioned = await new DeviceProvisioningService(db).ProvisionAsync(
            $"esp32-{Guid.NewGuid():N}",
            $"SN-{Guid.NewGuid():N}",
            Guid.NewGuid().ToString("N"));
        provisioned.Device.OwnerUserId = ownerUserId;
        await db.SaveChangesAsync();
        return (provisioned.Device.Id, provisioned.Compartments.Single(x => x.SlotNumber == 1).Id);
    }

    private async Task<Setup> SeedAsync()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var relative = await TestUsers.SeedAsync(fixture);
        var stranger = await TestUsers.SeedAsync(fixture);
        var (deviceId, slot1Id) = await ProvisionAsync(owner.Id);

        await using var db = fixture.CreateContext();
        db.CareRelationships.Add(new CareRelationship
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            RelativeUserId = relative.Id,
            Status = RelationshipStatus.Active,
            AcceptedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return new Setup(deviceId, slot1Id, owner, relative, stranger);
    }

    private sealed record Setup(
        Guid DeviceId,
        Guid CompartmentSlot1Id,
        ApplicationUser Owner,
        ApplicationUser Relative,
        ApplicationUser Stranger);
}
