namespace SmartMedBox.Infrastructure.Schedules;

public interface IDoseDeadlineConnectivityPolicy
{
    bool WasOnlineAtDeadline(DateTimeOffset deadlineAt, DateTimeOffset? lastSeenAt);
}
