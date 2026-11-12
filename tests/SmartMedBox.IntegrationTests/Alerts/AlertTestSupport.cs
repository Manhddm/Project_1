using System.Collections.Concurrent;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Alerts;
using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Alerts;

// The evaluation job and email worker scan whole tables, so every test gets its own database.
internal sealed class AlertTestDatabase : IAsyncDisposable
{
    // D-12 default (5 minutes); D-13 retry values below are test-only.
    public static readonly TimeSpan OnlineThreshold = new DeviceConnectivityOptions().OfflineThreshold;
    public const int MaxAttempts = 3;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly SqlServerDatabaseFixture _fixture = new();

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero));

    public static async Task<AlertTestDatabase> CreateAsync()
    {
        var database = new AlertTestDatabase();
        await database._fixture.InitializeAsync();
        return database;
    }

    public AppDbContext CreateContext() => _fixture.CreateContext();

    public AlertEvaluationService CreateEvaluator(AppDbContext db) =>
        new(db, Clock, new DeviceConnectivityService(db, Options.Create(new DeviceConnectivityOptions())));

    public EmailDeliveryWorker CreateWorker(AppDbContext db, IEmailSender sender) =>
        new(db, Clock, sender,
            new ConfiguredEmailRetryPolicy(new EmailRetryPolicyOptions { MaxAttempts = MaxAttempts, RetryDelays = [RetryDelay, RetryDelay] }),
            new EmailDeliveryWorkerOptions { LeaseDuration = TimeSpan.FromMinutes(5) });

    /// <summary>A non-null <paramref name="lastSeenAt"/> also seeds one connectivity window that has been open for a day.</summary>
    public async Task<Box> SeedBoxAsync(DateTimeOffset? lastSeenAt, int activeRelatives = 2, int revokedRelatives = 1)
    {
        await using var db = CreateContext();
        var owner = NewUser("owner");
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceId = $"device-{Guid.NewGuid():N}", SerialNumber = $"serial-{Guid.NewGuid():N}",
            DeviceApiKeyHash = "not-a-real-hash", OwnerUserId = owner.Id, LastSeenAt = lastSeenAt,
        };
        var compartment = new Compartment { Id = Guid.NewGuid(), DeviceId = device.Id, SlotNumber = 1, Name = "Sáng", IsEnabled = true };
        var schedule = new MedicationSchedule
        {
            Id = Guid.NewGuid(), DeviceId = device.Id, CompartmentId = compartment.Id, TimeOfDay = new TimeOnly(7, 0),
            StartDate = new DateOnly(2026, 1, 1), GracePeriodMinutes = 30, TimeZoneId = "Asia/Ho_Chi_Minh",
        };
        db.ApplicationUsers.Add(owner);
        db.Devices.Add(device);
        if (lastSeenAt is { } seenAt)
        {
            db.DeviceConnectivityWindows.Add(new DeviceConnectivityWindow
            {
                Id = Guid.NewGuid(), DeviceId = device.Id, OnlineFrom = seenAt.AddDays(-1), LastSeenAt = seenAt,
            });
        }

        db.Compartments.Add(compartment);
        db.MedicationSchedules.Add(schedule);

        var active = new List<ApplicationUser>();
        var revoked = new List<ApplicationUser>();
        for (var i = 0; i < activeRelatives + revokedRelatives; i++)
        {
            var isActive = i < activeRelatives;
            var relative = NewUser(isActive ? "relative" : "revoked");
            (isActive ? active : revoked).Add(relative);
            db.ApplicationUsers.Add(relative);
            db.CareRelationships.Add(new CareRelationship
            {
                Id = Guid.NewGuid(), DeviceId = device.Id, RelativeUserId = relative.Id,
                Status = isActive ? RelationshipStatus.Active : RelationshipStatus.Revoked,
                AcceptedAt = Clock.UtcNow.AddDays(-10), RevokedAt = isActive ? null : Clock.UtcNow.AddDays(-1),
            });
        }

        await db.SaveChangesAsync();
        return new Box(device.Id, compartment.Id, schedule.Id, owner, active, revoked);
    }

    public async Task<Guid> SeedDoseAsync(Box box, DateTimeOffset deadlineAt, DoseStatus status = DoseStatus.Pending)
    {
        await using var db = CreateContext();
        var dose = new DoseInstance
        {
            Id = Guid.NewGuid(), ScheduleId = box.ScheduleId, DeviceId = box.DeviceId, CompartmentId = box.CompartmentId,
            ScheduledAt = deadlineAt.AddMinutes(-30), GracePeriodMinutes = 30, DeadlineAt = deadlineAt, Status = status,
            ConfirmedAt = status == DoseStatus.Confirmed ? deadlineAt.AddMinutes(-10) : null,
        };
        db.DoseInstances.Add(dose);
        await db.SaveChangesAsync();
        return dose.Id;
    }

    public async Task SeedWindowAsync(Box box, DateTimeOffset onlineFrom, DateTimeOffset lastSeenAt)
    {
        await using var db = CreateContext();
        db.DeviceConnectivityWindows.Add(new DeviceConnectivityWindow
        {
            Id = Guid.NewGuid(), DeviceId = box.DeviceId, OnlineFrom = onlineFrom, LastSeenAt = lastSeenAt,
        });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    private ApplicationUser NewUser(string prefix) => new()
    {
        Id = Guid.NewGuid(), Email = $"{prefix}-{Guid.NewGuid():N}@example.com", PasswordHash = "not-a-real-hash",
        EmailVerified = true, CreatedAt = Clock.UtcNow.AddDays(-30),
    };
}

internal sealed record Box(
    Guid DeviceId, Guid CompartmentId, Guid ScheduleId, ApplicationUser Owner,
    IReadOnlyList<ApplicationUser> ActiveRelatives, IReadOnlyList<ApplicationUser> RevokedRelatives)
{
    public IEnumerable<Guid> ExpectedRecipientIds => ActiveRelatives.Select(x => x.Id).Append(Owner.Id);
}

// Fails the first N sends per address (int.MaxValue = always fails); records every successful send.
internal sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, int> _remainingFailures = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public ConcurrentQueue<string> FailedAttempts { get; } = new();
    public TimeSpan SendDelay { get; init; } = TimeSpan.Zero;

    public void FailFor(string address, int times = int.MaxValue) => _remainingFailures[address] = times;

    public async Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (SendDelay > TimeSpan.Zero) await Task.Delay(SendDelay, cancellationToken);
        if (_remainingFailures.TryGetValue(message.To, out var remaining) && remaining > 0)
        {
            _remainingFailures[message.To] = remaining - 1;
            FailedAttempts.Enqueue(message.To);
            throw new InvalidOperationException("Simulated provider failure.");
        }

        Sent.Enqueue(message);
        var messageId = EmailHeaders.NewMessageId("fake@test.local");
        MessageIds.Enqueue(messageId);
        return messageId;
    }

    public ConcurrentQueue<string> MessageIds { get; } = new();

    public int SentCount(string address) => Sent.Count(x => string.Equals(x.To, address, StringComparison.OrdinalIgnoreCase));
}
