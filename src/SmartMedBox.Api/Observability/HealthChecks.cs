using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Storage;

namespace SmartMedBox.Api.Observability;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    // CanConnectAsync nuốt lỗi kết nối và trả false; kết quả không mang exception/connection string.
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
}

public sealed class FileStorageHealthCheck(IOptions<FileStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // LocalFileStorage tạo thư mục khi ghi lần đầu, nên ở đây cũng tạo rồi thử ghi/xóa một file thăm dò.
            var root = Path.GetFullPath(options.Value.RootPath);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
            await File.WriteAllBytesAsync(probe, [], cancellationToken);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Không đưa exception vào kết quả: message chứa đường dẫn.
            return HealthCheckResult.Unhealthy();
        }
    }
}

public static class HealthEndpoints
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
            .AddCheck<FileStorageHealthCheck>("file-storage", tags: [ReadyTag]);
        return services;
    }

    // Response writer mặc định chỉ ghi "Healthy"/"Unhealthy" dạng text/plain — không lộ chi tiết.
    public static void MapAppHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
    }
}
