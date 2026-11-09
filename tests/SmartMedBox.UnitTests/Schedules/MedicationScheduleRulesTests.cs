using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Schedules;

namespace SmartMedBox.UnitTests.Schedules;

public sealed class MedicationScheduleRulesTests
{
    [Fact]
    public void EnsureValid_ValidSchedule_DoesNotThrow()
    {
        MedicationScheduleRules.EnsureValid(NewSchedule(), [1, 2, 3, 4, 5, 6, 7]);
        MedicationScheduleRules.EnsureValid(NewSchedule(endDate: new DateOnly(2026, 1, 1)), [1]);
    }

    [Fact]
    public void EnsureValid_EndDateBeforeStartDate_Throws()
    {
        var schedule = NewSchedule(endDate: new DateOnly(2025, 12, 31));

        Assert.Throws<ArgumentException>(() => MedicationScheduleRules.EnsureValid(schedule, [1]));
    }

    [Fact]
    public void EnsureValid_NoDays_Throws()
    {
        Assert.Throws<ArgumentException>(() => MedicationScheduleRules.EnsureValid(NewSchedule(), []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void EnsureValid_DayOutsideOneToSeven_Throws(int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MedicationScheduleRules.EnsureValid(NewSchedule(), [day]));
    }

    [Fact]
    public void EnsureValid_DuplicateDays_Throws()
    {
        Assert.Throws<ArgumentException>(() => MedicationScheduleRules.EnsureValid(NewSchedule(), [1, 1]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void EnsureValid_GraceOutsideOneToOneTwenty_Throws(int grace)
    {
        var schedule = NewSchedule();
        schedule.GracePeriodMinutes = grace;

        Assert.Throws<ArgumentOutOfRangeException>(() => MedicationScheduleRules.EnsureValid(schedule, [1]));
    }

    [Fact]
    public void EnsureValid_EmptyTimeZone_Throws()
    {
        var schedule = NewSchedule();
        schedule.TimeZoneId = " ";

        Assert.Throws<ArgumentException>(() => MedicationScheduleRules.EnsureValid(schedule, [1]));
    }

    private static MedicationSchedule NewSchedule(DateOnly? endDate = null) => new()
    {
        DeviceId = Guid.NewGuid(),
        CompartmentId = Guid.NewGuid(),
        TimeOfDay = new TimeOnly(7, 0),
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = endDate,
        GracePeriodMinutes = 15,
        TimeZoneId = "Asia/Ho_Chi_Minh",
    };
}
