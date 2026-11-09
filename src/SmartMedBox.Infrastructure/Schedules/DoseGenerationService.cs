using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Schedules;

public sealed record DoseGenerationResult(int GeneratedCount, int ExistingCount);

public sealed class DoseGenerationService(AppDbContext db)
{
    public async Task<DoseGenerationResult> GenerateAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive <= fromInclusive)
        {
            throw new ArgumentException("The generation range must be non-empty.", nameof(toExclusive));
        }

        var schedules = await db.MedicationSchedules
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .ToListAsync(cancellationToken);
        var scheduleIds = schedules.Select(x => x.Id).ToArray();
        var days = await db.ScheduleDays.AsNoTracking()
            .Where(x => scheduleIds.Contains(x.ScheduleId))
            .ToListAsync(cancellationToken);
        var items = await db.ScheduleItems.AsNoTracking()
            .Where(x => scheduleIds.Contains(x.ScheduleId))
            .ToListAsync(cancellationToken);

        var generatedCount = 0;
        var existingCount = 0;
        foreach (var schedule in schedules)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var scheduleDays = days.Where(x => x.ScheduleId == schedule.Id)
                .Select(x => x.DayOfWeek).ToHashSet();
            var scheduleItems = items.Where(x => x.ScheduleId == schedule.Id).ToArray();

            foreach (var scheduledAt in EnumerateOccurrences(schedule, scheduleDays, timeZone, fromInclusive, toExclusive))
            {
                var dose = new DoseInstance
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = schedule.Id,
                    DeviceId = schedule.DeviceId,
                    CompartmentId = schedule.CompartmentId,
                    ScheduledAt = scheduledAt,
                    GracePeriodMinutes = schedule.GracePeriodMinutes,
                    DeadlineAt = scheduledAt.AddMinutes(schedule.GracePeriodMinutes),
                    Status = DoseStatus.Pending,
                };
                db.DoseInstances.Add(dose);
                db.DoseItemSnapshots.AddRange(scheduleItems.Select(item => new DoseItemSnapshot
                {
                    Id = Guid.NewGuid(),
                    DoseInstanceId = dose.Id,
                    MedicineName = item.MedicineName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    Instructions = item.Instructions,
                }));

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    generatedCount++;
                }
                catch (DbUpdateException exception) when (IsUniqueOccurrenceViolation(exception))
                {
                    db.ChangeTracker.Clear();
                    existingCount++;
                }
            }
        }

        return new DoseGenerationResult(generatedCount, existingCount);
    }

    private static IEnumerable<DateTimeOffset> EnumerateOccurrences(
        MedicationSchedule schedule,
        IReadOnlySet<int> scheduleDays,
        TimeZoneInfo timeZone,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive)
    {
        var localStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fromInclusive.AddDays(-1), timeZone).DateTime);
        var localEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(toExclusive.AddDays(1), timeZone).DateTime);
        for (var localDate = localStart; localDate <= localEnd; localDate = localDate.AddDays(1))
        {
            if (localDate < schedule.StartDate
                || schedule.EndDate is { } endDate && localDate > endDate
                || !scheduleDays.Contains(ToIsoDayOfWeek(localDate.DayOfWeek)))
            {
                continue;
            }

            var localDateTime = localDate.ToDateTime(schedule.TimeOfDay);
            if (timeZone.IsInvalidTime(localDateTime))
            {
                continue;
            }

            var occurrence = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone), TimeSpan.Zero);
            if (occurrence >= fromInclusive && occurrence < toExclusive)
            {
                yield return occurrence;
            }
        }
    }

    private static int ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;

    private static bool IsUniqueOccurrenceViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
