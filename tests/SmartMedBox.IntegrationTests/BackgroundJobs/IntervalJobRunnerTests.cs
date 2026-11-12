using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SmartMedBox.Api.BackgroundJobs;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.BackgroundJobs;

public sealed class IntervalJobRunnerTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ExecuteAsync_ShortInterval_RunsJobRepeatedlyInNewScopeEachRun()
    {
        var scopes = new ConcurrentBag<Marker>();
        var thirdRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = CreateRunner((sp, _) =>
        {
            scopes.Add(sp.GetRequiredService<Marker>());
            if (scopes.Count == 3) thirdRun.TrySetResult();
            return Task.FromResult(false);
        });

        await runner.StartAsync(CancellationToken.None);
        await thirdRun.Task.WaitAsync(WaitLimit);
        await runner.StopAsync(CancellationToken.None);

        Assert.Equal(scopes.Count, scopes.Distinct().Count());
    }

    [Fact]
    public async Task ExecuteAsync_JobThrows_LogsAndKeepsRunning()
    {
        var runs = 0;
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = CreateRunner((_, _) =>
        {
            if (Interlocked.Increment(ref runs) == 1) throw new InvalidOperationException("boom");
            secondRun.TrySetResult();
            return Task.FromResult(false);
        });

        await runner.StartAsync(CancellationToken.None);
        await secondRun.Task.WaitAsync(WaitLimit);
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_JobReportsMoreWork_RunsAgainWithoutWaitingForInterval()
    {
        var runs = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runner = CreateRunner((_, _) =>
        {
            var run = Interlocked.Increment(ref runs);
            if (run == 5) done.TrySetResult();
            return Task.FromResult(run < 5);
        }, interval: TimeSpan.FromHours(1));

        await runner.StartAsync(CancellationToken.None);
        await done.Task.WaitAsync(WaitLimit);
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_JobBlockedOnToken_CancelsJobAndStops()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var runner = CreateRunner(async (_, ct) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return false;
        });

        await runner.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(WaitLimit);
        await runner.StopAsync(CancellationToken.None).WaitAsync(WaitLimit);

        Assert.True(cancelled);
        Assert.True(runner.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public void AddBackgroundJobs_DisabledInTestHost_RegistersNoRunner()
    {
        using var factory = new TestApplicationFactory();

        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), x => x is IntervalJobRunner);
    }

    [Fact]
    public void AddBackgroundJobs_Enabled_RegistersThreeRunnersAndD13Defaults()
    {
        using var provider = BuildProvider(new() { ["BackgroundJobs:Enabled"] = "true" });

        var names = provider.GetServices<IHostedService>().OfType<IntervalJobRunner>().Select(x => x.Name).Order();
        Assert.Equal(["AlertEvaluation", "DoseGeneration", "EmailDelivery"], names);
        Assert.Equal(5, provider.GetRequiredService<IEmailRetryPolicy>().MaxAttempts);
        Assert.Equal(TimeSpan.FromMinutes(2), provider.GetRequiredService<EmailDeliveryWorkerOptions>().LeaseDuration);
    }

    [Fact]
    public void AddBackgroundJobs_RetryDelaysConfigured_ReplacesDefaults()
    {
        using var provider = BuildProvider(new()
        {
            ["BackgroundJobs:Enabled"] = "false",
            ["EmailRetryPolicy:MaxAttempts"] = "2",
            ["EmailRetryPolicy:RetryDelays:0"] = "00:00:30",
        });

        Assert.Equal([TimeSpan.FromSeconds(30)], provider.GetRequiredService<EmailRetryPolicyOptions>().RetryDelays);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings) =>
        new ServiceCollection().AddLogging()
            .AddBackgroundJobs(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

    private static IntervalJobRunner CreateRunner(
        Func<IServiceProvider, CancellationToken, Task<bool>> job, TimeSpan? interval = null)
    {
        var provider = new ServiceCollection().AddScoped<Marker>().BuildServiceProvider();
        return new IntervalJobRunner("Test", interval ?? TimeSpan.FromMilliseconds(20), job,
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<IntervalJobRunner>.Instance);
    }

    private sealed class Marker;
}
