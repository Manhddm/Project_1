using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Schedules;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.UnitTests.Schedules;

public sealed class DoseStateMachineTests
{
    private static readonly DateTimeOffset ScheduledAt = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompartmentId = Guid.NewGuid();

    [Fact]
    public void ApplyLidOpened_WithinWindow_ConfirmsPendingDose()
    {
        var dose = NewDose();
        var occurredAt = ScheduledAt.AddMinutes(5);
        var machine = CreateMachine(occurredAt);

        var result = machine.ApplyLidOpened(dose, CompartmentId, occurredAt);

        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(occurredAt, dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.Confirmed, result.Outcome);
        Assert.Equal(DoseTransitionReason.LidOpenedInWindow, result.Reason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void ApplyLidOpened_OutsideWindow_DoesNotConfirm(int minutesFromScheduled)
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(30));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(minutesFromScheduled));

        Assert.Equal(DoseStatus.Pending, dose.Status);
        Assert.Null(dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.Ignored, result.Outcome);
        Assert.Equal(DoseTransitionReason.OutsideConfirmationWindow, result.Reason);
    }

    [Fact]
    public void ApplyLidOpened_WrongCompartment_DoesNotConfirm()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(5));

        var result = machine.ApplyLidOpened(dose, Guid.NewGuid(), ScheduledAt.AddMinutes(5));

        Assert.Equal(DoseStatus.Pending, dose.Status);
        Assert.Equal(DoseTransitionReason.WrongCompartment, result.Reason);
    }

    [Fact]
    public void ApplyLidOpened_RepeatedValidOpen_PreservesFirstConfirmation()
    {
        var dose = NewDose();
        var firstOpenedAt = ScheduledAt.AddMinutes(5);
        var machine = CreateMachine(firstOpenedAt);
        machine.ApplyLidOpened(dose, CompartmentId, firstOpenedAt);

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(firstOpenedAt, dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.NoChange, result.Outcome);
        Assert.Equal(DoseTransitionReason.AlreadyConfirmed, result.Reason);
    }

    [Fact]
    public void ApplyLidOpened_AfterOverdue_DoesNotChangeStatus()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(31));
        machine.EvaluateDeadline(dose, ScheduledAt.AddMinutes(30));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        Assert.Equal(DoseStatus.Overdue, dose.Status);
        Assert.Equal(DoseTransitionOutcome.Ignored, result.Outcome);
        Assert.Equal(DoseTransitionReason.DoseAlreadyResolved, result.Reason);
    }

    [Fact]
    public void EvaluateDeadline_BeforeDeadline_LeavesDosePending()
    {
        var dose = NewDose();
        var policy = new RecordingConnectivityPolicy(true);
        var machine = new DoseStateMachine(new FakeClock(ScheduledAt.AddMinutes(10)), policy);

        var result = machine.EvaluateDeadline(dose, ScheduledAt.AddMinutes(29));

        Assert.Equal(DoseStatus.Pending, dose.Status);
        Assert.Equal(DoseTransitionReason.DeadlineNotReached, result.Reason);
        Assert.False(policy.WasCalled);
    }

    [Fact]
    public void EvaluateDeadline_OnlineAtDeadline_MarksOverdueUsingDeadlineContext()
    {
        var dose = NewDose();
        var policy = new RecordingConnectivityPolicy(true);
        var machine = new DoseStateMachine(new FakeClock(ScheduledAt.AddHours(2)), policy);
        var lastSeenAt = ScheduledAt.AddMinutes(20);

        var result = machine.EvaluateDeadline(dose, lastSeenAt);

        Assert.Equal(DoseStatus.Overdue, dose.Status);
        Assert.Equal(DoseTransitionOutcome.Overdue, result.Outcome);
        Assert.Equal(DoseTransitionReason.OnlineAtDeadline, result.Reason);
        Assert.Equal(dose.DeadlineAt, policy.DeadlineAt);
        Assert.Equal(lastSeenAt, policy.LastSeenAt);
    }

    [Fact]
    public void EvaluateDeadline_OfflineAtDeadline_MarksUnknown()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(31), wasOnlineAtDeadline: false);

        var result = machine.EvaluateDeadline(dose, null);

        Assert.Equal(DoseStatus.Unknown, dose.Status);
        Assert.Equal(DoseTransitionOutcome.Unknown, result.Outcome);
        Assert.Equal(DoseTransitionReason.OfflineAtDeadline, result.Reason);
    }

    [Theory]
    [InlineData(DoseStatus.Overdue)]
    [InlineData(DoseStatus.Unknown)]
    public void EvaluateDeadline_ResolvedDose_DoesNotChangeStatus(DoseStatus status)
    {
        var dose = NewDose();
        dose.Status = status;
        var machine = CreateMachine(ScheduledAt.AddHours(2));

        var result = machine.EvaluateDeadline(dose, ScheduledAt.AddMinutes(30));

        Assert.Equal(status, dose.Status);
        Assert.Equal(DoseTransitionOutcome.NoChange, result.Outcome);
        Assert.Equal(DoseTransitionReason.DoseAlreadyResolved, result.Reason);
    }

    [Theory]
    [InlineData(DoseStatus.Overdue)]
    [InlineData(DoseStatus.Unknown)]
    public void ApplyLidOpened_LateConfirmation_DoesNotChangeResolvedDose(DoseStatus status)
    {
        var dose = NewDose();
        dose.Status = status;
        var machine = CreateMachine(ScheduledAt.AddMinutes(10));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        Assert.Equal(status, dose.Status);
        Assert.Equal(DoseTransitionOutcome.Ignored, result.Outcome);
        Assert.Equal(DoseTransitionReason.DoseAlreadyResolved, result.Reason);
        Assert.Null(dose.ConfirmedAt);
    }

    [Fact]
    public void LastSeenAtThresholdPolicy_UsesDeadlineAndConfiguredThreshold()
    {
        var policy = new LastSeenAtThresholdPolicy(TimeSpan.FromMinutes(10));
        var deadline = ScheduledAt.AddMinutes(30);

        Assert.True(policy.WasOnlineAtDeadline(deadline, deadline.AddMinutes(-10)));
        Assert.False(policy.WasOnlineAtDeadline(deadline, deadline.AddMinutes(-11)));
        Assert.False(policy.WasOnlineAtDeadline(deadline, null));
    }

    [Fact]
    public void LastSeenAtThresholdPolicy_NegativeThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LastSeenAtThresholdPolicy(TimeSpan.FromSeconds(-1)));
    }

    private static DoseStateMachine CreateMachine(
        DateTimeOffset now,
        bool wasOnlineAtDeadline = true) =>
        new(new FakeClock(now), new RecordingConnectivityPolicy(wasOnlineAtDeadline));

    private static DoseInstance NewDose() => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        CompartmentId = CompartmentId,
        ScheduledAt = ScheduledAt,
        GracePeriodMinutes = 15,
        DeadlineAt = ScheduledAt.AddMinutes(15),
        Status = DoseStatus.Pending,
    };

    private sealed class RecordingConnectivityPolicy(bool result) : IDoseDeadlineConnectivityPolicy
    {
        public bool WasCalled { get; private set; }
        public DateTimeOffset DeadlineAt { get; private set; }
        public DateTimeOffset? LastSeenAt { get; private set; }

        public bool WasOnlineAtDeadline(DateTimeOffset deadlineAt, DateTimeOffset? lastSeenAt)
        {
            WasCalled = true;
            DeadlineAt = deadlineAt;
            LastSeenAt = lastSeenAt;
            return result;
        }
    }
}
