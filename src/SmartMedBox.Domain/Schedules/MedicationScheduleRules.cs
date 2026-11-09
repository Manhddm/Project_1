using SmartMedBox.Domain.Entities;

namespace SmartMedBox.Domain.Schedules;

public static class MedicationScheduleRules
{
    public const int MinGracePeriodMinutes = 1;
    public const int MaxGracePeriodMinutes = 120;
    public const int MinDayOfWeek = 1;
    public const int MaxDayOfWeek = 7;

    public static void EnsureValid(MedicationSchedule schedule, IReadOnlyCollection<int> daysOfWeek)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(daysOfWeek);

        if (schedule.GracePeriodMinutes is < MinGracePeriodMinutes or > MaxGracePeriodMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedule),
                schedule.GracePeriodMinutes,
                $"Grace period must be between {MinGracePeriodMinutes} and {MaxGracePeriodMinutes} minutes.");
        }

        if (schedule.EndDate is { } endDate && endDate < schedule.StartDate)
        {
            throw new ArgumentException("End date must be on or after start date.", nameof(schedule));
        }

        if (string.IsNullOrWhiteSpace(schedule.TimeZoneId))
        {
            throw new ArgumentException("Time zone is required.", nameof(schedule));
        }

        if (daysOfWeek.Count == 0)
        {
            throw new ArgumentException("At least one day of week is required.", nameof(daysOfWeek));
        }

        foreach (var day in daysOfWeek)
        {
            if (day is < MinDayOfWeek or > MaxDayOfWeek)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(daysOfWeek),
                    day,
                    $"Day of week must be between {MinDayOfWeek} and {MaxDayOfWeek}.");
            }
        }

        if (daysOfWeek.Distinct().Count() != daysOfWeek.Count)
        {
            throw new ArgumentException("Days of week must not contain duplicates.", nameof(daysOfWeek));
        }
    }
}
