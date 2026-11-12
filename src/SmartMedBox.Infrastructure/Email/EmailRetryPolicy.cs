namespace SmartMedBox.Infrastructure.Email;

/// <summary>D-13 defaults: 5 attempts, waits of 1 → 5 → 15 → 60 minutes before attempts 2..5, then Exhausted.</summary>
public sealed class EmailRetryPolicyOptions
{
    public int MaxAttempts { get; init; } = 5;

    /// <summary>RetryDelays[i] is the wait after failed attempt i+1; needs at least MaxAttempts - 1 entries.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];
}

public interface IEmailRetryPolicy
{
    int MaxAttempts { get; }
    DateTimeOffset NextAttemptAt(DateTimeOffset failedAt, int failedAttemptNumber);
}

public sealed class ConfiguredEmailRetryPolicy(EmailRetryPolicyOptions options) : IEmailRetryPolicy
{
    private readonly EmailRetryPolicyOptions _options = Validate(options);
    public int MaxAttempts => _options.MaxAttempts;

    public DateTimeOffset NextAttemptAt(DateTimeOffset failedAt, int failedAttemptNumber)
    {
        if (failedAttemptNumber < 1 || failedAttemptNumber >= MaxAttempts) throw new ArgumentOutOfRangeException(nameof(failedAttemptNumber));
        return failedAt.Add(_options.RetryDelays[failedAttemptNumber - 1]);
    }

    private static EmailRetryPolicyOptions Validate(EmailRetryPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxAttempts < 1 || options.RetryDelays is null || options.RetryDelays.Count < options.MaxAttempts - 1
            || options.RetryDelays.Any(x => x < TimeSpan.Zero)) throw new ArgumentOutOfRangeException(nameof(options));
        return options;
    }
}
