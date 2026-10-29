using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.IntegrationTests.Schema;

public sealed class SchemaConstraintTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly string[] ExpectedTables =
    [
        "AlertRecipients", "Alerts", "ApplicationUsers", "AuditLogs", "CareRelationships",
        "Compartments", "DeviceEvents", "DevicePairingCodes", "Devices", "DoseInstances",
        "DoseItemSnapshots", "EmailDeliveries", "MedicationSchedules", "PrescriptionImages",
        "RelativeInvitations", "ScheduleDays", "ScheduleItems",
    ];

    [Fact]
    public async Task Migration_CreatesAllSeventeenTables()
    {
        await using var db = fixture.CreateContext();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        var applicationTables = tables.Except(["__EFMigrationsHistory"]).Order().ToArray();
        Assert.Equal(ExpectedTables.Order(), applicationTables);
    }

    [Fact]
    public async Task Devices_NullOwnerAndLastSeen_AreAllowed()
    {
        await using var db = fixture.CreateContext();
        var device = NewDevice();

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        var stored = await db.Devices.AsNoTracking().SingleAsync(x => x.Id == device.Id);
        Assert.Null(stored.OwnerUserId);
        Assert.Null(stored.LastSeenAt);
    }

    [Fact]
    public async Task Devices_SecondDeviceForSameOwner_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var user = NewUser();
        var first = NewDevice(user.Id);

        db.ApplicationUsers.Add(user);
        db.Devices.Add(first);
        await db.SaveChangesAsync();

        db.Devices.Add(NewDevice(user.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Compartments_DuplicateSlot_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var device = NewDevice();
        db.Devices.Add(device);
        db.Compartments.Add(NewCompartment(device.Id, 1));
        await db.SaveChangesAsync();

        db.Compartments.Add(NewCompartment(device.Id, 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Compartments_SlotOutsideOneToThree_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var device = NewDevice();
        db.Devices.Add(device);

        db.Compartments.Add(NewCompartment(device.Id, 4));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeviceEvents_DuplicateEventIdForSameDevice_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var (device, compartment) = await SeedDeviceAsync(db);

        db.DeviceEvents.Add(NewEvent(device.Id, compartment.Id, "evt-1"));
        await db.SaveChangesAsync();

        db.DeviceEvents.Add(NewEvent(device.Id, compartment.Id, "evt-1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeviceEvents_InvalidEventType_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var (device, compartment) = await SeedDeviceAsync(db);
        var evt = NewEvent(device.Id, compartment.Id, "evt-2");
        db.DeviceEvents.Add(evt);
        await db.SaveChangesAsync();

        evt.EventType = (DeviceEventType)99;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DoseInstances_InvalidStatus_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);

        dose.Status = (DoseStatus)99;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DoseInstances_DuplicateScheduleOccurrence_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);

        db.DoseInstances.Add(new DoseInstance
        {
            Id = Guid.NewGuid(),
            ScheduleId = dose.ScheduleId,
            DeviceId = dose.DeviceId,
            CompartmentId = dose.CompartmentId,
            ScheduledAt = dose.ScheduledAt,
            GracePeriodMinutes = 15,
            DeadlineAt = dose.DeadlineAt,
            Status = DoseStatus.Pending,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Alerts_SecondAlertOfSameTypeForDose_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);
        db.Alerts.Add(NewAlert(dose.Id, AlertType.OverdueUnconfirmed));
        await db.SaveChangesAsync();

        db.Alerts.Add(NewAlert(dose.Id, AlertType.OverdueUnconfirmed));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AlertRecipients_DuplicateRecipientChannel_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);
        var recipientUser = NewUser();
        db.ApplicationUsers.Add(recipientUser);
        var alert = NewAlert(dose.Id, AlertType.UnconfirmedOffline);
        db.Alerts.Add(alert);
        db.AlertRecipients.Add(NewRecipient(alert.Id, recipientUser.Id));
        await db.SaveChangesAsync();

        db.AlertRecipients.Add(NewRecipient(alert.Id, recipientUser.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EmailDeliveries_DuplicateAttemptNumber_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);
        var recipientUser = NewUser();
        db.ApplicationUsers.Add(recipientUser);
        var alert = NewAlert(dose.Id, AlertType.OverdueUnconfirmed);
        db.Alerts.Add(alert);
        var recipient = NewRecipient(alert.Id, recipientUser.Id);
        db.AlertRecipients.Add(recipient);
        await db.SaveChangesAsync();

        db.EmailDeliveries.Add(NewDelivery(recipient.Id, attemptNumber: 1));
        await db.SaveChangesAsync();

        db.EmailDeliveries.Add(NewDelivery(recipient.Id, attemptNumber: 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CareRelationships_ActivePairIsUnique_RevokedPairIsAllowed()
    {
        await using var db = fixture.CreateContext();
        var (device, _) = await SeedDeviceAsync(db);
        var relative = NewUser();
        db.ApplicationUsers.Add(relative);
        db.CareRelationships.Add(NewRelationship(device.Id, relative.Id, RelationshipStatus.Active));
        await db.SaveChangesAsync();

        db.CareRelationships.Add(NewRelationship(device.Id, relative.Id, RelationshipStatus.Active));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var retry = fixture.CreateContext();
        retry.CareRelationships.Add(NewRelationship(device.Id, relative.Id, RelationshipStatus.Revoked));
        await retry.SaveChangesAsync();
    }

    [Fact]
    public async Task RelativeInvitations_SecondPendingInviteForSameEmail_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var (device, _) = await SeedDeviceAsync(db);
        const string email = "relative@example.com";

        db.RelativeInvitations.Add(NewInvitation(device.Id, email, InvitationStatus.Pending));
        await db.SaveChangesAsync();

        db.RelativeInvitations.Add(NewInvitation(device.Id, email, InvitationStatus.Pending));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ScheduleDays_DuplicateDay_IsRejected()
    {
        await using var db = fixture.CreateContext();
        var dose = await SeedDoseAsync(db);
        db.ScheduleDays.Add(new ScheduleDay { ScheduleId = dose.ScheduleId, DayOfWeek = 2 });
        await db.SaveChangesAsync();

        await using var retry = fixture.CreateContext();
        retry.ScheduleDays.Add(new ScheduleDay { ScheduleId = dose.ScheduleId, DayOfWeek = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => retry.SaveChangesAsync());
    }

    [Theory]
    [InlineData("IX_DoseInstances_Status_DeadlineAt")]
    [InlineData("IX_DeviceEvents_DeviceId_OccurredAt")]
    [InlineData("IX_EmailDeliveries_Status_NextAttemptAt")]
    [InlineData("IX_MedicationSchedules_DeviceId_IsDeleted")]
    public async Task HotPathIndex_Exists(string indexName)
    {
        await using var db = fixture.CreateContext();

        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = {0}", indexName)
            .SingleAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PrescriptionImages_StoresMetadataOnly()
    {
        await using var db = fixture.CreateContext();

        var binaryColumns = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT c.[name] AS [Value]
                FROM sys.columns AS c
                JOIN sys.types AS t ON t.[user_type_id] = c.[user_type_id]
                WHERE c.[object_id] = OBJECT_ID(N'PrescriptionImages')
                  AND t.[name] IN (N'varbinary', N'image')
                """)
            .ToListAsync();

        Assert.Empty(binaryColumns);
    }

    [Fact]
    public async Task ApplicationUsers_DuplicateEmail_IsRejected()
    {
        await using var db = fixture.CreateContext();
        db.ApplicationUsers.Add(NewUser("owner@example.com"));
        await db.SaveChangesAsync();

        db.ApplicationUsers.Add(NewUser("owner@example.com"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static async Task<(Device Device, Compartment Compartment)> SeedDeviceAsync(AppDbContext db)
    {
        var device = NewDevice();
        var compartment = NewCompartment(device.Id, 1);
        db.Devices.Add(device);
        db.Compartments.Add(compartment);
        await db.SaveChangesAsync();
        return (device, compartment);
    }

    private static async Task<DoseInstance> SeedDoseAsync(AppDbContext db)
    {
        var (device, compartment) = await SeedDeviceAsync(db);
        var schedule = NewSchedule(device.Id, compartment.Id);
        db.MedicationSchedules.Add(schedule);

        var dose = NewDose(schedule.Id, device.Id, compartment.Id);
        db.DoseInstances.Add(dose);
        await db.SaveChangesAsync();
        return dose;
    }

    private static ApplicationUser NewUser(string? email = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = email ?? $"user-{Guid.NewGuid():N}@example.com",
        PasswordHash = "not-a-real-hash",
        EmailVerified = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Device NewDevice(Guid? ownerUserId = null) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = $"device-{Guid.NewGuid():N}",
        SerialNumber = $"serial-{Guid.NewGuid():N}",
        DeviceApiKeyHash = "not-a-real-hash",
        OwnerUserId = ownerUserId,
        ScheduleVersion = 0,
    };

    private static Compartment NewCompartment(Guid deviceId, int slotNumber) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        SlotNumber = slotNumber,
        Name = $"Slot {slotNumber}",
        IsEnabled = true,
    };

    private static DeviceEvent NewEvent(Guid deviceId, Guid compartmentId, string eventId) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        EventId = eventId,
        CompartmentId = compartmentId,
        EventType = DeviceEventType.LidOpened,
        OccurredAt = DateTimeOffset.UtcNow,
        ReceivedAt = DateTimeOffset.UtcNow,
    };

    private static MedicationSchedule NewSchedule(Guid deviceId, Guid compartmentId) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        CompartmentId = compartmentId,
        TimeOfDay = new TimeOnly(7, 0),
        StartDate = new DateOnly(2026, 1, 1),
        GracePeriodMinutes = 15,
        TimeZoneId = "Asia/Ho_Chi_Minh",
    };

    private static DoseInstance NewDose(Guid scheduleId, Guid deviceId, Guid compartmentId)
    {
        var scheduledAt = DateTimeOffset.UtcNow;
        return new DoseInstance
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            DeviceId = deviceId,
            CompartmentId = compartmentId,
            ScheduledAt = scheduledAt,
            GracePeriodMinutes = 15,
            DeadlineAt = scheduledAt.AddMinutes(15),
            Status = DoseStatus.Pending,
        };
    }

    private static Alert NewAlert(Guid doseInstanceId, AlertType alertType) => new()
    {
        Id = Guid.NewGuid(),
        DoseInstanceId = doseInstanceId,
        AlertType = alertType,
        Message = "Dose was not confirmed.",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static AlertRecipient NewRecipient(Guid alertId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        AlertId = alertId,
        RecipientUserId = userId,
        Channel = "Email",
    };

    private static EmailDelivery NewDelivery(Guid alertRecipientId, int attemptNumber) => new()
    {
        Id = Guid.NewGuid(),
        AlertRecipientId = alertRecipientId,
        AttemptNumber = attemptNumber,
        Status = EmailDeliveryStatus.Pending,
    };

    private static CareRelationship NewRelationship(Guid deviceId, Guid relativeUserId, RelationshipStatus status) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        RelativeUserId = relativeUserId,
        Status = status,
        AcceptedAt = DateTimeOffset.UtcNow,
        RevokedAt = status == RelationshipStatus.Revoked ? DateTimeOffset.UtcNow : null,
    };

    private static RelativeInvitation NewInvitation(Guid deviceId, string email, InvitationStatus status) => new()
    {
        Id = Guid.NewGuid(),
        DeviceId = deviceId,
        InviteeEmail = email,
        TokenHash = $"hash-{Guid.NewGuid():N}",
        Status = status,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
    };
}
