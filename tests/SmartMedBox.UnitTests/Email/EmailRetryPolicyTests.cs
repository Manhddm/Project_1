using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.UnitTests.Email;

public sealed class EmailRetryPolicyTests
{
    [Fact]
    public void Defaults_FollowD13Schedule()
    {
        var policy = new ConfiguredEmailRetryPolicy(new EmailRetryPolicyOptions());
        var failedAt = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal([1, 5, 15, 60], Enumerable.Range(1, 4).Select(n => (policy.NextAttemptAt(failedAt, n) - failedAt).TotalMinutes));
        // No 6th attempt: the worker marks the 5th failure Exhausted.
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.NextAttemptAt(failedAt, 5));
        Assert.Equal(TimeSpan.FromMinutes(2), new EmailDeliveryWorkerOptions().LeaseDuration);
    }

    [Fact]
    public void Constructor_FewerDelaysThanRetries_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ConfiguredEmailRetryPolicy(new EmailRetryPolicyOptions { MaxAttempts = 3, RetryDelays = [TimeSpan.FromMinutes(1)] }));
}
