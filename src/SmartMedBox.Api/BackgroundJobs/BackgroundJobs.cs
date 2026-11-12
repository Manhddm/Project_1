using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Alerts;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.Infrastructure.Schedules;

namespace SmartMedBox.Api.BackgroundJobs;

public sealed class BackgroundJobsOptions
{
    public const string SectionName = "BackgroundJobs";

    public bool Enabled { get; set; } = true;

    /// <summary>D-11: 15 phút, phạm vi [now − 24 h, now + 48 h].</summary>
    public TimeSpan DoseGenerationInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>[ĐỀ XUẤT] 1 phút — chưa có trong quyết định đã chốt.</summary>
    public TimeSpan AlertEvaluationInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>[ĐỀ XUẤT] chờ 10 giây khi không còn delivery đến hạn.</summary>
    public TimeSpan EmailDeliveryIdleDelay { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Chạy một job theo chu kỳ, mỗi lần một DI scope mới. Job trả <c>true</c> = còn việc, chạy lại ngay (scope mới).
/// Không có khóa phân tán: các job đã an toàn khi chạy song song nhiều instance (AGENTS §8).
/// </summary>
public sealed class IntervalJobRunner(
    string name,
    TimeSpan interval,
    Func<IServiceProvider, CancellationToken, Task<bool>> job,
    IServiceScopeFactory scopes,
    ILogger<IntervalJobRunner> logger) : BackgroundService
{
    public string Name => name;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                while (await RunOnceAsync(stoppingToken))
                {
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await job(scope.ServiceProvider, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Chỉ log loại lỗi: message (vd. SQL) có thể chứa email/dữ liệu sức khỏe (AGENTS §7).
            logger.LogError("Background job {Job} failed with {ExceptionType}.", name, ex.GetType().FullName);
            return false;
        }
    }
}

public static class BackgroundJobsServiceCollectionExtensions
{
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DeviceEventTimeOptions>(configuration.GetSection("DeviceEventTime"));
        services.AddScoped<AlertEvaluationService>();
        services.AddScoped<EmailDeliveryWorker>();
        // Binder would append configured RetryDelays to the D-13 defaults, so the list is replaced explicitly.
        var retry = configuration.GetSection("EmailRetryPolicy");
        var retryDefaults = new EmailRetryPolicyOptions();
        services.AddSingleton(new EmailRetryPolicyOptions
        {
            MaxAttempts = retry.GetValue("MaxAttempts", retryDefaults.MaxAttempts),
            RetryDelays = retry.GetSection("RetryDelays").Get<TimeSpan[]>() ?? retryDefaults.RetryDelays,
        });
        services.AddSingleton<IEmailRetryPolicy, ConfiguredEmailRetryPolicy>();
        services.AddSingleton(configuration.GetSection("EmailDeliveryWorker").Get<EmailDeliveryWorkerOptions>() ?? new EmailDeliveryWorkerOptions());

        var options = configuration.GetSection(BackgroundJobsOptions.SectionName).Get<BackgroundJobsOptions>() ?? new BackgroundJobsOptions();
        if (!options.Enabled)
        {
            return services;
        }

        Add("DoseGeneration", options.DoseGenerationInterval, async (sp, ct) =>
        {
            var now = sp.GetRequiredService<IClock>().UtcNow;
            await sp.GetRequiredService<DoseGenerationService>().GenerateAsync(now.AddHours(-24), now.AddHours(48), ct);
            return false;
        });
        Add("AlertEvaluation", options.AlertEvaluationInterval, async (sp, ct) =>
        {
            await sp.GetRequiredService<AlertEvaluationService>().EvaluateAsync(ct);
            return false;
        });
        Add("EmailDelivery", options.EmailDeliveryIdleDelay, async (sp, ct) =>
            await sp.GetRequiredService<EmailDeliveryWorker>().ProcessNextAsync(ct) != EmailDeliveryResult.NoDelivery);
        return services;

        // AddHostedService dedupes by type, so three runners of the same type are added as IHostedService directly.
        void Add(string name, TimeSpan interval, Func<IServiceProvider, CancellationToken, Task<bool>> job) =>
            services.AddSingleton<IHostedService>(sp => new IntervalJobRunner(
                name, interval, job, sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<IntervalJobRunner>>()));
    }
}
