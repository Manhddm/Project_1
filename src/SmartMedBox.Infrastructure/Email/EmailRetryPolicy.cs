namespace SmartMedBox.Infrastructure.Email;

public sealed class EmailRetryPolicyOptions
{
    public required int MaxAttempts { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public required double BackoffMultiplier { get; init; }
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
        return failedAt.Add(TimeSpan.FromTicks((long)(_options.InitialDelay.Ticks * Math.Pow(_options.BackoffMultiplier, failedAttemptNumber - 1))));
    }

    private static EmailRetryPolicyOptions Validate(EmailRetryPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxAttempts < 1 || options.InitialDelay < TimeSpan.Zero || options.BackoffMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(options));
        return options;
    }
}
