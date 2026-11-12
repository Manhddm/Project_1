using System.Net;
using System.Net.Http.Json;
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
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Devices;

/// <summary>P4-03: ghép LidOpened với DoseInstance qua POST /api/device/events/batch (AGENTS §4.1, §4.2).</summary>
public sealed class DeviceEventMatchingTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>, IDisposable
{
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    public void Dispose() => _factories.ForEach(x => x.Dispose());

    private const string Url = "/api/device/events/batch";

    // Dose slot 2 (Trưa): 12:00–12:15 UTC.
    private static readonly DateTimeOffset Scheduled = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Scheduled.AddMinutes(15);

    private readonly FakeClock _clock = new(Scheduled.AddMinutes(30));

    [Fact]
    public async Task EventsBatch_LidOpenedBeforeScheduledAt_DoesNotConfirm()
    {
        var setup = await SetupAsync();

        var results = await SendAsync(setup, Event("early", 2, "LidOpened", Scheduled.AddMinutes(-1)));

        AssertAcceptedUnmatched(results.Single());
        await AssertDoseAsync(setup.DoseId, DoseStatus.Pending, null);
        await AssertStoredUnmatchedAsync(setup, "early");
    }

    [Fact]
    public async Task EventsBatch_LidOpenedWrongCompartmentInWindow_DoesNotConfirm()
    {
        var setup = await SetupAsync();

        var results = await SendAsync(setup, Event("wrong-slot", 1, "LidOpened", Scheduled.AddMinutes(5)));

        AssertAcceptedUnmatched(results.Single());
        await AssertDoseAsync(setup.DoseId, DoseStatus.Pending, null);
        await AssertStoredUnmatchedAsync(setup, "wrong-slot");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(15)]
    public async Task EventsBatch_LidOpenedCorrectCompartmentInWindow_ConfirmsAtOccurredAt(int minutes)
    {
        var setup = await SetupAsync();
        var occurredAt = Scheduled.AddMinutes(minutes);

        var results = await SendAsync(setup, Event("open", 2, "LidOpened", occurredAt));

        AssertConfirmed(results.Single(), "Accepted", setup.DoseId);
        await AssertDoseAsync(setup.DoseId, DoseStatus.Confirmed, occurredAt);
        await using var db = fixture.CreateContext();
        Assert.Equal(setup.DoseId, (await db.DeviceEvents.SingleAsync(x => x.DeviceId == setup.DeviceKey)).MatchedDoseInstanceId);
    }

    [Fact]
    public async Task EventsBatch_LidOpenedAfterDeadline_DoesNotConfirm()
    {
        var setup = await SetupAsync();

        var results = await SendAsync(setup, Event("late", 2, "LidOpened", Deadline.AddSeconds(1)));

        AssertAcceptedUnmatched(results.Single());
        await AssertDoseAsync(setup.DoseId, DoseStatus.Pending, null);
    }

    [Fact]
    public async Task EventsBatch_MultipleValidOpens_OnlyFirstConfirmsAndAllStored()
    {
        var setup = await SetupAsync();

        var first = await SendAsync(setup,
            Event("open-1", 2, "LidOpened", Scheduled.AddMinutes(3)),
            Event("close-1", 2, "LidClosed", Scheduled.AddMinutes(4)),
            Event("open-2", 2, "LidOpened", Scheduled.AddMinutes(8)));
        var later = await SendAsync(setup, Event("open-3", 2, "LidOpened", Scheduled.AddMinutes(10)));

        AssertConfirmed(first[0], "Accepted", setup.DoseId);
        AssertAcceptedUnmatched(first[1]);
        AssertAcceptedUnmatched(first[2]);
        AssertAcceptedUnmatched(later.Single());
        await AssertDoseAsync(setup.DoseId, DoseStatus.Confirmed, Scheduled.AddMinutes(3));
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.AsNoTracking().Where(x => x.DeviceId == setup.DeviceKey)
            .OrderBy(x => x.EventId).Select(x => new { x.EventId, x.MatchedDoseInstanceId }).ToListAsync();
        Assert.Equal(["close-1", "open-1", "open-2", "open-3"], stored.Select(x => x.EventId));
        Assert.Equal([null, setup.DoseId, null, null], stored.Select(x => x.MatchedDoseInstanceId));
    }

    [Fact]
    public async Task EventsBatch_LidClosedInWindow_DoesNotConfirmButIsStored()
    {
        var setup = await SetupAsync();

        var results = await SendAsync(setup, Event("closed", 2, "LidClosed", Scheduled.AddMinutes(5)));

        AssertAcceptedUnmatched(results.Single());
        await AssertDoseAsync(setup.DoseId, DoseStatus.Pending, null);
        await AssertStoredUnmatchedAsync(setup, "closed");
    }

    [Fact]
    public async Task EventsBatch_SameBatchSentThreeTimes_OneRowPerEventOneConfirmationAndDuplicates()
    {
        var setup = await SetupAsync();
        object[] batch =
        [
            Event("r-open-1", 2, "LidOpened", Scheduled.AddMinutes(2)),
            Event("r-close-1", 2, "LidClosed", Scheduled.AddMinutes(3)),
            Event("r-open-2", 2, "LidOpened", Scheduled.AddMinutes(6)),
        ];

        var first = await SendAsync(setup, batch);
        var second = await SendAsync(setup, batch);
        var third = await SendAsync(setup, batch);

        AssertConfirmed(first[0], "Accepted", setup.DoseId);
        Assert.All(first.Skip(1), AssertAcceptedUnmatched);
        foreach (var resend in new[] { second, third })
        {
            Assert.All(resend, r => Assert.Equal("Duplicate", r.Status));
            AssertConfirmed(resend[0], "Duplicate", setup.DoseId);
            Assert.All(resend.Skip(1), r => Assert.Null(r.MatchedDoseInstanceId));
        }

        await AssertDoseAsync(setup.DoseId, DoseStatus.Confirmed, Scheduled.AddMinutes(2));
        await using var db = fixture.CreateContext();
        Assert.Equal(3, await db.DeviceEvents.CountAsync(x => x.DeviceId == setup.DeviceKey));
        Assert.Equal(1, await db.DeviceEvents.CountAsync(x => x.DeviceId == setup.DeviceKey && x.MatchedDoseInstanceId != null));
    }

    [Fact]
    public async Task EventsBatch_ShuffledBatchOrder_ConfirmsEarliestOccurredAt()
    {
        var a = await SetupAsync();
        var b = await SetupAsync();
        var inOrder = new[]
        {
            Event("x-open-1", 2, "LidOpened", Scheduled.AddMinutes(4)),
            Event("x-close-1", 2, "LidClosed", Scheduled.AddMinutes(5)),
            Event("x-open-2", 2, "LidOpened", Scheduled.AddMinutes(9)),
            Event("x-open-3", 2, "LidOpened", Scheduled.AddMinutes(12)),
        };
        var shuffled = new[] { inOrder[3], inOrder[1], inOrder[0], inOrder[2] };

        var ra = await SendAsync(a, inOrder);
        var rb = await SendAsync(b, shuffled);

        AssertConfirmed(ra[0], "Accepted", a.DoseId);
        AssertConfirmed(rb[2], "Accepted", b.DoseId);
        Assert.Equal(1, ra.Count(r => r.MatchedDoseInstanceId is not null));
        Assert.Equal(1, rb.Count(r => r.MatchedDoseInstanceId is not null));
        await AssertDoseAsync(a.DoseId, DoseStatus.Confirmed, Scheduled.AddMinutes(4));
        await AssertDoseAsync(b.DoseId, DoseStatus.Confirmed, Scheduled.AddMinutes(4));
    }

    [Fact]
    public async Task EventsBatch_OfflineEventArrivesLateInWindow_StillConfirms()
    {
        var setup = await SetupAsync();
        _clock.UtcNow = Scheduled.AddHours(6); // thiết bị reconnect sau 6 giờ, gửi lại hàng đợi offline

        var results = await SendAsync(setup, Event("offline-open", 2, "LidOpened", Scheduled.AddMinutes(7)));

        AssertConfirmed(results.Single(), "Accepted", setup.DoseId);
        await AssertDoseAsync(setup.DoseId, DoseStatus.Confirmed, Scheduled.AddMinutes(7));
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.SingleAsync(x => x.DeviceId == setup.DeviceKey);
        Assert.Equal(Scheduled.AddMinutes(7), stored.OccurredAt);
        Assert.Equal(Scheduled.AddHours(6), stored.ReceivedAt);
    }

    // [TBD-D24] Mặc định: xác nhận muộn sau Overdue/Unknown không đổi trạng thái, không set ConfirmedAt.
    [Theory]
    [InlineData(DoseStatus.Overdue)]
    [InlineData(DoseStatus.Unknown)]
    public async Task EventsBatch_InWindowOpenArrivesAfterDoseResolved_StatusUnchanged(DoseStatus resolved)
    {
        var setup = await SetupAsync(resolved);
        _clock.UtcNow = Scheduled.AddHours(2);

        var results = await SendAsync(setup, Event("after-resolved", 2, "LidOpened", Scheduled.AddMinutes(5)));

        AssertAcceptedUnmatched(results.Single());
        await AssertDoseAsync(setup.DoseId, resolved, null);
        await AssertStoredUnmatchedAsync(setup, "after-resolved");
    }

    private sealed record Setup(WebApplicationFactory<Program> Factory, ProvisioningResult Device, Guid DoseId)
    {
        public Guid DeviceKey => Device.Provisioned.Device.Id;
    }

    private sealed record ResultRow(string EventId, string Status, Guid? MatchedDoseInstanceId, string? DoseStatus);

    private async Task<Setup> SetupAsync(DoseStatus status = DoseStatus.Pending)
    {
        var factory = new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
            }));
        _factories.Add(factory);
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        var owner = await TestUsers.SeedAsync(fixture);
        await using var db = fixture.CreateContext();
        var deviceKey = device.Provisioned.Device.Id;
        (await db.Devices.SingleAsync(x => x.Id == deviceKey)).OwnerUserId = owner.Id;
        var compartmentId = await db.Compartments.Where(x => x.DeviceId == deviceKey && x.SlotNumber == 2)
            .Select(x => x.Id).SingleAsync();
        var schedule = new MedicationSchedule
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceKey,
            CompartmentId = compartmentId,
            TimeOfDay = new TimeOnly(12, 0),
            StartDate = DateOnly.FromDateTime(Scheduled.UtcDateTime),
            GracePeriodMinutes = 15,
            TimeZoneId = "UTC",
        };
        var dose = new DoseInstance
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            DeviceId = deviceKey,
            CompartmentId = compartmentId,
            ScheduledAt = Scheduled,
            GracePeriodMinutes = 15,
            DeadlineAt = Deadline,
            Status = status,
        };
        db.MedicationSchedules.Add(schedule);
        db.DoseInstances.Add(dose);
        await db.SaveChangesAsync();
        return new Setup(factory, device, dose.Id);
    }

    private static object Event(string eventId, int slot, string type, DateTimeOffset occurredAt) =>
        new { eventId, compartmentSlot = slot, eventType = type, occurredAt };

    private static async Task<List<ResultRow>> SendAsync(Setup setup, params object[] events)
    {
        using var client = TestUsers.CreateClient(setup.Factory);
        using var request = DeviceAuthenticationTests.DeviceRequest(
            setup.Device.Provisioned.Device.DeviceId, setup.Device.DeviceApiKey, Url);
        request.Content = JsonContent.Create(new { batchId = Guid.NewGuid().ToString(), events });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => new ResultRow(
                r.GetProperty("eventId").GetString()!,
                r.GetProperty("status").GetString()!,
                r.GetProperty("matchedDoseInstanceId").ValueKind == JsonValueKind.Null
                    ? null
                    : r.GetProperty("matchedDoseInstanceId").GetGuid(),
                r.GetProperty("doseStatus").GetString()))
            .ToList();
    }

    private static void AssertAcceptedUnmatched(ResultRow row)
    {
        Assert.Equal("Accepted", row.Status);
        Assert.Null(row.MatchedDoseInstanceId);
        Assert.Null(row.DoseStatus);
    }

    private static void AssertConfirmed(ResultRow row, string status, Guid doseId)
    {
        Assert.Equal(status, row.Status);
        Assert.Equal(doseId, row.MatchedDoseInstanceId);
        Assert.Equal("Confirmed", row.DoseStatus);
    }

    private async Task AssertDoseAsync(Guid doseId, DoseStatus status, DateTimeOffset? confirmedAt)
    {
        await using var db = fixture.CreateContext();
        var dose = await db.DoseInstances.AsNoTracking().SingleAsync(x => x.Id == doseId);
        Assert.Equal(status, dose.Status);
        Assert.Equal(confirmedAt, dose.ConfirmedAt);
    }

    private async Task AssertStoredUnmatchedAsync(Setup setup, string eventId)
    {
        await using var db = fixture.CreateContext();
        var stored = await db.DeviceEvents.AsNoTracking()
            .SingleAsync(x => x.DeviceId == setup.DeviceKey && x.EventId == eventId);
        Assert.Null(stored.MatchedDoseInstanceId);
    }
}
