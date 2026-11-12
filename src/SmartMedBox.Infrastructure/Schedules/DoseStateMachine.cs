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
    LateConfirmation,
    EarlierOpenInWindow,
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

public sealed class DoseStateMachine(IClock clock)
{
    public DoseTransitionResult ApplyLidOpened(
        DoseInstance dose,
        Guid compartmentId,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(dose);

        var previousStatus = dose.Status;
        if (dose.CompartmentId != compartmentId)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.Ignored, DoseTransitionReason.WrongCompartment);
        }

        if (occurredAt < dose.ScheduledAt || occurredAt > dose.DeadlineAt)
        {
            return Result(dose, previousStatus, DoseTransitionOutcome.Ignored, DoseTransitionReason.OutsideConfirmationWindow);
        }

        // D-24d: lần mở hợp lệ đầu tiên tính theo occurredAt sớm nhất, kể cả khi đến ở batch sau.
        if (dose.Status == DoseStatus.Confirmed)
        {
            if (dose.ConfirmedAt is { } confirmedAt && confirmedAt <= occurredAt)
            {
                return Result(dose, previousStatus, DoseTransitionOutcome.NoChange, DoseTransitionReason.AlreadyConfirmed);
            }

            dose.ConfirmedAt = occurredAt;
            return Result(dose, previousStatus, DoseTransitionOutcome.Confirmed, DoseTransitionReason.EarlierOpenInWindow);
        }

        // D-24a: Overdue/Unknown được xác nhận muộn khi event trong cửa sổ đến sau khi đã đánh giá hạn.
        var reason = dose.Status == DoseStatus.Pending
            ? DoseTransitionReason.LidOpenedInWindow
            : DoseTransitionReason.LateConfirmation;
        dose.Status = DoseStatus.Confirmed;
        dose.ConfirmedAt = occurredAt;
        return Result(dose, previousStatus, DoseTransitionOutcome.Confirmed, reason);
    }

    /// <param name="onlineAtDeadline">From <c>DeviceConnectivityService.WasOnlineAtAsync</c> (D-12).</param>
    public DoseTransitionResult EvaluateDeadline(DoseInstance dose, bool onlineAtDeadline)
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

        var online = onlineAtDeadline;
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
