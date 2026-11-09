using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Alerts;

public sealed class AlertEvaluationOptions
{
    public required TimeSpan OnlineThreshold { get; init; }
}

public enum AlertConnectivity
{
    Online,
    Offline,
}

public interface IAlertConnectivityPolicy
{
    AlertConnectivity Evaluate(DateTimeOffset deadlineAt, DateTimeOffset? lastSeenAt);
}

public sealed class DeadlineConnectivityPolicy(AlertEvaluationOptions options) : IAlertConnectivityPolicy
{
    private readonly TimeSpan _onlineThreshold = Validate(options).OnlineThreshold;

    public AlertConnectivity Evaluate(DateTimeOffset deadlineAt, DateTimeOffset? lastSeenAt) =>
        lastSeenAt is not null && lastSeenAt.Value >= deadlineAt - _onlineThreshold
            ? AlertConnectivity.Online
            : AlertConnectivity.Offline;

    private static AlertEvaluationOptions Validate(AlertEvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.OnlineThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        return options;
    }
}
