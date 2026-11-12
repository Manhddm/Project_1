using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Schedules;
using SmartMedBox.Infrastructure.Time;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Schedules;

/// <summary>
/// P3-03 (AC-10, D-22). GenerateAsync quét mọi lịch trong DB dùng chung của class này,
/// nên mỗi test dùng một năm riêng (StartDate/EndDate + khoảng sinh) để không đếm nhầm dose của test khác.
/// </summary>
public sealed class DoseGenerationTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private static readonly int[] EveryDay = [1, 2, 3, 4, 5, 6, 7];

    [Fact]
    public async Task GenerateAsync_HoChiMinhScheduleWithDaysAndDateWindow_StoresUtcOccurrencesWithDeadline()
    {
        // 06:00 tại UTC+7 rơi vào 23:00 UTC ngày hôm trước: thứ trong tuần phải tính theo ngày địa phương.
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(6, 0), days: [1, 3],
            start: new DateOnly(2031, 3, 4), end: new DateOnly(2031, 3, 12), grace: 45);

        var result = await GenerateAsync(Utc(2031, 3, 1), Utc(2031, 3, 20));

        Assert.Equal(new DoseGenerationResult(3, 0), result);
        var doses = await DosesAsync(seed.ScheduleId);
        Assert.Equal(
            [Utc(2031, 3, 4, 23), Utc(2031, 3, 9, 23), Utc(2031, 3, 11, 23)],
            doses.Select(x => x.ScheduledAt));
        Assert.All(doses, dose =>
        {
            Assert.Equal(TimeSpan.Zero, dose.ScheduledAt.Offset);
            Assert.Equal(dose.ScheduledAt.AddMinutes(45), dose.DeadlineAt);
            Assert.Equal(45, dose.GracePeriodMinutes);
            Assert.Equal(DoseStatus.Pending, dose.Status);
            Assert.Null(dose.ConfirmedAt);
            Assert.Equal(seed.DeviceId, dose.DeviceId);
            Assert.Equal(seed.CompartmentId, dose.CompartmentId);
        });
    }

    [Fact]
    public async Task GenerateAsync_NewYorkScheduleAcrossDstStart_KeepsLocalTimeOfDay()
    {
        // DST Mỹ bắt đầu 2032-03-14: 08:00 địa phương = 13:00Z trước đó, 12:00Z sau đó.
        var seed = await SeedAsync(
            "America/New_York", new TimeOnly(8, 0), EveryDay,
            start: new DateOnly(2032, 3, 13), end: new DateOnly(2032, 3, 15));

        await GenerateAsync(Utc(2032, 3, 10), Utc(2032, 3, 20));

        Assert.Equal(
            [Utc(2032, 3, 13, 13), Utc(2032, 3, 14, 12), Utc(2032, 3, 15, 12)],
            (await DosesAsync(seed.ScheduleId)).Select(x => x.ScheduledAt));
    }

    [Fact]
    public async Task GenerateAsync_ScheduleEditedThenSoftDeleted_GeneratedDosesAndSnapshotsUnchanged()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(7, 0), EveryDay,
            start: new DateOnly(2033, 1, 1), end: new DateOnly(2033, 1, 2), grace: 30,
            items: [("Paracetamol", 1.5m, "viên", "Sau ăn"), ("Vitamin C", 2m, "viên", "")]);

        Assert.Equal(new DoseGenerationResult(2, 0), await GenerateAsync(Utc(2032, 12, 31), Utc(2033, 1, 5)));
        var dosesBefore = await DosesAsync(seed.ScheduleId);
        var snapshotsBefore = await SnapshotsAsync(seed.ScheduleId);
        Assert.Equal(4, snapshotsBefore.Count);
        foreach (var dose in dosesBefore)
        {
            Assert.Equal(
                [("Paracetamol", 1.5m, "viên", "Sau ăn"), ("Vitamin C", 2m, "viên", "")],
                snapshotsBefore.Where(x => x.DoseInstanceId == dose.Id)
                    .OrderBy(x => x.MedicineName)
                    .Select(x => (x.MedicineName, x.Quantity, x.Unit, x.Instructions)));
        }

        await using (var db = fixture.CreateContext())
        {
            var schedules = new ScheduleService(db, new DeviceAccessService(db), new SystemClock());
            var updated = await schedules.UpdateAsync(
                seed.OwnerId,
                seed.ScheduleId,
                new ScheduleInput(
                    CompartmentSlot: 2,
                    TimeOfDay: new TimeOnly(20, 0),
                    DaysOfWeek: [1],
                    StartDate: new DateOnly(2033, 1, 1),
                    EndDate: new DateOnly(2033, 1, 2),
                    GracePeriodMinutes: 90,
                    TimeZoneId: "Asia/Ho_Chi_Minh",
                    Items: [new ScheduleItemInput("Canxi", 3m, "gói", "Trước ngủ")]));
            Assert.Equal(ScheduleOutcome.Ok, updated.Outcome);
            Assert.Equal(ScheduleOutcome.Ok, await schedules.DeleteAsync(seed.OwnerId, seed.ScheduleId));
        }

        Assert.Equivalent(dosesBefore, await DosesAsync(seed.ScheduleId), strict: true);
        Assert.Equivalent(snapshotsBefore, await SnapshotsAsync(seed.ScheduleId), strict: true);
        Assert.Equal(new DoseGenerationResult(0, 0), await GenerateAsync(Utc(2032, 12, 31), Utc(2033, 1, 5)));
    }

    [Fact]
    public async Task GenerateAsync_RunTwiceOverSameRange_DoesNotDuplicate()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(12, 0), EveryDay,
            start: new DateOnly(2034, 5, 1), end: new DateOnly(2034, 5, 7));

        Assert.Equal(new DoseGenerationResult(7, 0), await GenerateAsync(Utc(2034, 4, 30), Utc(2034, 5, 10)));
        Assert.Equal(new DoseGenerationResult(0, 7), await GenerateAsync(Utc(2034, 4, 30), Utc(2034, 5, 10)));

        Assert.Equal(7, (await DosesAsync(seed.ScheduleId)).Count);
        Assert.Equal(7, (await SnapshotsAsync(seed.ScheduleId)).Count);
    }

    [Fact]
    public async Task GenerateAsync_TwoConcurrentRuns_GenerateEachOccurrenceOnce()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(7, 0), EveryDay,
            start: new DateOnly(2035, 6, 1), end: new DateOnly(2035, 6, 30));
        var noon = await AddScheduleAsync(seed, slot: 2, new TimeOnly(12, 0));
        var evening = await AddScheduleAsync(seed, slot: 3, new TimeOnly(19, 0));

        await using var db1 = fixture.CreateContext();
        await using var db2 = fixture.CreateContext();
        var results = await Task.WhenAll(
            new DoseGenerationService(db1).GenerateAsync(Utc(2035, 5, 31), Utc(2035, 7, 1)),
            new DoseGenerationService(db2).GenerateAsync(Utc(2035, 5, 31), Utc(2035, 7, 1)));

        Assert.All(results, x => Assert.Equal(90, x.GeneratedCount + x.ExistingCount));
        Assert.Equal(90, results.Sum(x => x.GeneratedCount));
        foreach (var scheduleId in new[] { seed.ScheduleId, noon, evening })
        {
            var doses = await DosesAsync(scheduleId);
            Assert.Equal(30, doses.Count);
            Assert.Equal(30, doses.Select(x => x.ScheduledAt).Distinct().Count());
            Assert.Equal(30, (await SnapshotsAsync(scheduleId)).Count);
        }
    }

    [Fact]
    public async Task GenerateAsync_CatchUpAfterDowntime_FillsGapWithoutDuplicating()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(9, 0), EveryDay,
            start: new DateOnly(2036, 2, 1), end: new DateOnly(2036, 2, 10));

        Assert.Equal(new DoseGenerationResult(3, 0), await GenerateAsync(Utc(2036, 2, 1), Utc(2036, 2, 4)));
        Assert.Equal(new DoseGenerationResult(7, 3), await GenerateAsync(Utc(2036, 1, 25), Utc(2036, 2, 15)));

        var doses = await DosesAsync(seed.ScheduleId);
        Assert.Equal(
            Enumerable.Range(1, 10).Select(day => Utc(2036, 2, day, 2)),
            doses.Select(x => x.ScheduledAt));
        Assert.Equal(10, (await SnapshotsAsync(seed.ScheduleId)).Count);
    }

    [Fact]
    public async Task GenerateAsync_SoftDeletedSchedule_GeneratesNothing()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(7, 0), EveryDay,
            start: new DateOnly(2037, 1, 1), end: new DateOnly(2037, 1, 5));
        await using (var db = fixture.CreateContext())
        {
            await db.MedicationSchedules.Where(x => x.Id == seed.ScheduleId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true).SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow));
        }

        Assert.Equal(new DoseGenerationResult(0, 0), await GenerateAsync(Utc(2036, 12, 31), Utc(2037, 1, 10)));
        Assert.Empty(await DosesAsync(seed.ScheduleId));
    }

    [Fact]
    public async Task GenerateAsync_UnpairedDevice_GeneratesNothing()
    {
        var seed = await SeedAsync(
            "Asia/Ho_Chi_Minh", new TimeOnly(7, 0), EveryDay,
            start: new DateOnly(2038, 1, 1), end: new DateOnly(2038, 1, 5), paired: false);

        Assert.Equal(new DoseGenerationResult(0, 0), await GenerateAsync(Utc(2037, 12, 31), Utc(2038, 1, 10)));
        Assert.Empty(await DosesAsync(seed.ScheduleId));
    }

    private sealed record Seeded(
        Guid OwnerId, Guid DeviceId, Guid CompartmentId, Guid ScheduleId, IReadOnlyList<Compartment> Compartments,
        DateOnly Start, DateOnly End);

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private async Task<DoseGenerationResult> GenerateAsync(DateTimeOffset from, DateTimeOffset to)
    {
        await using var db = fixture.CreateContext();
        return await new DoseGenerationService(db).GenerateAsync(from, to);
    }

    private async Task<List<DoseInstance>> DosesAsync(Guid scheduleId)
    {
        await using var db = fixture.CreateContext();
        return await db.DoseInstances.AsNoTracking()
            .Where(x => x.ScheduleId == scheduleId)
            .OrderBy(x => x.ScheduledAt)
            .ToListAsync();
    }

    private async Task<List<DoseItemSnapshot>> SnapshotsAsync(Guid scheduleId)
    {
        await using var db = fixture.CreateContext();
        var doseIds = db.DoseInstances.Where(x => x.ScheduleId == scheduleId).Select(x => x.Id);
        return await db.DoseItemSnapshots.AsNoTracking()
            .Where(x => doseIds.Contains(x.DoseInstanceId))
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    private async Task<Seeded> SeedAsync(
        string timeZoneId,
        TimeOnly timeOfDay,
        int[] days,
        DateOnly start,
        DateOnly end,
        int grace = 30,
        (string Name, decimal Quantity, string Unit, string Instructions)[]? items = null,
        bool paired = true)
    {
        var ownerId = paired ? (await TestUsers.SeedAsync(fixture)).Id : Guid.Empty;
        await using var db = fixture.CreateContext();
        var provisioned = await new DeviceProvisioningService(db).ProvisionAsync(
            $"esp32-{Guid.NewGuid():N}", $"SN-{Guid.NewGuid():N}", Guid.NewGuid().ToString("N"));
        if (paired)
        {
            provisioned.Device.OwnerUserId = ownerId;
        }

        var compartments = provisioned.Compartments.OrderBy(x => x.SlotNumber).ToList();
        var seed = new Seeded(ownerId, provisioned.Device.Id, compartments[0].Id, Guid.NewGuid(), compartments, start, end);
        AddSchedule(db, seed, seed.ScheduleId, compartments[0].Id, timeOfDay, days, grace, timeZoneId,
            items ?? [("Paracetamol", 1m, "viên", "")]);
        await db.SaveChangesAsync();
        return seed;
    }

    private async Task<Guid> AddScheduleAsync(Seeded seed, int slot, TimeOnly timeOfDay)
    {
        await using var db = fixture.CreateContext();
        var id = Guid.NewGuid();
        AddSchedule(db, seed, id, seed.Compartments[slot - 1].Id, timeOfDay, EveryDay, 30, "Asia/Ho_Chi_Minh",
            [("Vitamin D", 1m, "viên", "")]);
        await db.SaveChangesAsync();
        return id;
    }

    private static void AddSchedule(
        Infrastructure.Data.AppDbContext db,
        Seeded seed,
        Guid scheduleId,
        Guid compartmentId,
        TimeOnly timeOfDay,
        int[] days,
        int grace,
        string timeZoneId,
        (string Name, decimal Quantity, string Unit, string Instructions)[] items)
    {
        db.MedicationSchedules.Add(new MedicationSchedule
        {
            Id = scheduleId,
            DeviceId = seed.DeviceId,
            CompartmentId = compartmentId,
            TimeOfDay = timeOfDay,
            StartDate = seed.Start,
            EndDate = seed.End,
            GracePeriodMinutes = grace,
            TimeZoneId = timeZoneId,
        });
        db.ScheduleDays.AddRange(days.Select(day => new ScheduleDay { ScheduleId = scheduleId, DayOfWeek = day }));
        db.ScheduleItems.AddRange(items.Select(item => new ScheduleItem
        {
            Id = Guid.NewGuid(),
            ScheduleId = scheduleId,
            MedicineName = item.Name,
            Quantity = item.Quantity,
            Unit = item.Unit,
            Instructions = item.Instructions,
        }));
    }
}
