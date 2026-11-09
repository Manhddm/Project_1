namespace SmartMedBox.Infrastructure.Schedules;

public sealed class LastSeenAtThresholdPolicy(TimeSpan offlineThreshold) : IDoseDeadlineConnectivityPolicy
{
    public TimeSpan OfflineThreshold { get; } = ValidateThreshold(offlineThreshold);

    public bool WasOnlineAtDeadline(DateTimeOffset deadlineAt, DateTimeOffset? lastSeenAt) =>
        lastSeenAt is { } seenAt && seenAt >= deadlineAt - OfflineThreshold;

    private static TimeSpan ValidateThreshold(TimeSpan threshold)
    {
        if (threshold < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold cannot be negative.");
        }

        return threshold;
    }
}
