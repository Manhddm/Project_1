using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Schedules;

public enum DoseTransitionOutcome
{
    Confirmed,
    Overdue,
    Unknown,
    NoChange,
    Ignored,
}

public enum DoseTransitionReason
{
    LidOpenedInWindow,
    AlreadyConfirmed,
    DoseAlreadyResolved,
    WrongCompartment,
    OutsideConfirmationWindow,
    DeadlineNotReached,
    OnlineAtDeadline,
    OfflineAtDeadline,
}

public sealed record DoseTransitionResult(
    DoseStatus PreviousStatus,
    DoseStatus CurrentStatus,
    DoseTransitionOutcome Outcome,
    DoseTransitionReason Reason,
    DateTimeOffset? ConfirmedAt);

public sealed class DoseStateMachine(IClock clock, IDoseDeadlineConnectivityPolicy? connectivityPolicy = null)
{
    public DoseTransitionResult ApplyLidOpened(
        DoseInstance dose,
        Guid compartmentId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(dose);

        var previousStatus = dose.Status;
        if (dose.Status == DoseStatus.Confirmed)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.NoChange, DoseTransitionReason.AlreadyConfirmed);
        }

        if (dose.Status != DoseStatus.Pending)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.Ignored, DoseTransitionReason.DoseAlreadyResolved);
        }

        if (dose.CompartmentId != compartmentId)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.Ignored, DoseTransitionReason.WrongCompartment);
        }

        if (occurredAt < dose.ScheduledAt || occurredAt > dose.DeadlineAt)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.Ignored, DoseTransitionReason.OutsideConfirmationWindow);
        }

        dose.Status = DoseStatus.Confirmed;
        dose.ConfirmedAt = occurredAt;
        return Result(dose, previousStatus, DoseTransitionOutcome.Confirmed, DoseTransitionReason.LidOpenedInWindow);
    }

    public DoseTransitionResult EvaluateDeadline(DoseInstance dose, DateTimeOffset? lastSeenAt)
    {
        ArgumentNullException.ThrowIfNull(dose);

        var previousStatus = dose.Status;
        if (dose.Status != DoseStatus.Pending)
        {
            var reason = dose.Status == DoseStatus.Confirmed
                ? DoseTransitionReason.AlreadyConfirmed
                : DoseTransitionReason.DoseAlreadyResolved;
            return Result(dose, previousStatus, DoseTransitionOutcome.NoChange, reason);
        }

        if (clock.UtcNow <= dose.DeadlineAt)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.NoChange, DoseTransitionReason.DeadlineNotReached);
        }

        if (connectivityPolicy is null)
        {
            throw new InvalidOperationException("A deadline connectivity policy is required to evaluate a dose deadline.");
        }

        var online = connectivityPolicy.WasOnlineAtDeadline(dose.DeadlineAt, lastSeenAt);
        dose.Status = online ? DoseStatus.Overdue : DoseStatus.Unknown;
        return Result(
            dose,
            previousStatus,
            online ? DoseTransitionOutcome.Overdue : DoseTransitionOutcome.Unknown,
            online ? DoseTransitionReason.OnlineAtDeadline : DoseTransitionReason.OfflineAtDeadline);
    }

    private static DoseTransitionResult Result(
        DoseInstance dose,
        DoseStatus previousStatus,
        DoseTransitionOutcome outcome,
        DoseTransitionReason reason) =>
        new(previousStatus, dose.Status, outcome, reason, dose.ConfirmedAt);
}
