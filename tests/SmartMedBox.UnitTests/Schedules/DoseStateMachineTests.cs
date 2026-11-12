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
    public void ApplyLidOpened_EarlierValidOpenAfterConfirmation_MovesConfirmedAtBack()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(20));
        machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(4));

        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(ScheduledAt.AddMinutes(4), dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.Confirmed, result.Outcome);
        Assert.Equal(DoseTransitionReason.EarlierOpenInWindow, result.Reason);
        Assert.Equal(DoseStatus.Confirmed, result.PreviousStatus);
    }

    [Fact]
    public void ApplyLidOpened_EarlierOpenOutsideWindowAfterConfirmation_KeepsConfirmedAt()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(20));
        machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(-1));

        Assert.Equal(ScheduledAt.AddMinutes(10), dose.ConfirmedAt);
        Assert.Equal(DoseTransitionReason.OutsideConfirmationWindow, result.Reason);
    }

    [Fact]
    public void EvaluateDeadline_BeforeDeadline_LeavesDosePending()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(10));

        var result = machine.EvaluateDeadline(dose, onlineAtDeadline: true);

        Assert.Equal(DoseStatus.Pending, dose.Status);
        Assert.Equal(DoseTransitionReason.DeadlineNotReached, result.Reason);
    }

    [Fact]
    public void EvaluateDeadline_OnlineAtDeadline_MarksOverdue()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddHours(2));

        var result = machine.EvaluateDeadline(dose, onlineAtDeadline: true);

        Assert.Equal(DoseStatus.Overdue, dose.Status);
        Assert.Equal(DoseTransitionOutcome.Overdue, result.Outcome);
        Assert.Equal(DoseTransitionReason.OnlineAtDeadline, result.Reason);
    }

    [Fact]
    public void EvaluateDeadline_OfflineAtDeadline_MarksUnknown()
    {
        var dose = NewDose();
        var machine = CreateMachine(ScheduledAt.AddMinutes(31));

        var result = machine.EvaluateDeadline(dose, onlineAtDeadline: false);

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

        var result = machine.EvaluateDeadline(dose, onlineAtDeadline: true);

        Assert.Equal(status, dose.Status);
        Assert.Equal(DoseTransitionOutcome.NoChange, result.Outcome);
        Assert.Equal(DoseTransitionReason.DoseAlreadyResolved, result.Reason);
    }

    [Theory]
    [InlineData(DoseStatus.Overdue)]
    [InlineData(DoseStatus.Unknown)]
    public void ApplyLidOpened_InWindowOpenAfterResolved_ConfirmsLate(DoseStatus status)
    {
        var dose = NewDose();
        dose.Status = status;
        var machine = CreateMachine(ScheduledAt.AddHours(2));

        var result = machine.ApplyLidOpened(dose, CompartmentId, ScheduledAt.AddMinutes(10));

        Assert.Equal(DoseStatus.Confirmed, dose.Status);
        Assert.Equal(ScheduledAt.AddMinutes(10), dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.Confirmed, result.Outcome);
        Assert.Equal(DoseTransitionReason.LateConfirmation, result.Reason);
        Assert.Equal(status, result.PreviousStatus);
    }

    [Theory]
    [InlineData(DoseStatus.Overdue)]
    [InlineData(DoseStatus.Unknown)]
    public void ApplyLidOpened_OpenAfterDeadlineOnResolvedDose_DoesNotConfirm(DoseStatus status)
    {
        var dose = NewDose();
        dose.Status = status;
        var machine = CreateMachine(ScheduledAt.AddHours(2));

        var result = machine.ApplyLidOpened(dose, CompartmentId, dose.DeadlineAt.AddSeconds(1));

        Assert.Equal(status, dose.Status);
        Assert.Null(dose.ConfirmedAt);
        Assert.Equal(DoseTransitionOutcome.Ignored, result.Outcome);
        Assert.Equal(DoseTransitionReason.OutsideConfirmationWindow, result.Reason);
    }

    private static DoseStateMachine CreateMachine(DateTimeOffset now) => new(new FakeClock(now));

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
}
