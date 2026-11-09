using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Observability;

public sealed class TraceIdLogScopeTests
{
    [Fact]
    public async Task Request_Logged_ScopeCarriesProblemTraceIdAndOmitsQueryAndHeaders()
    {
        var logs = new CapturingLoggerProvider();
        using var baseFactory = new TestApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/khong-ton-tai?token=query-secret-123");
        request.Headers.Add("X-Probe-Secret", "header-secret-456");

        using var response = await client.SendAsync(request);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));

        var httpLogs = logs.Logs.Where(l => l.Category.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(httpLogs);
        Assert.All(httpLogs, log => Assert.Contains(log.ScopeValues, kv => kv.Key == "TraceId" && Equals(kv.Value, traceId)));

        Assert.DoesNotContain(logs.Logs, log => log.AllText.Contains("query-secret-123", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Logs, log => log.AllText.Contains("header-secret-456", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Logs, log => log.AllText.Contains("X-Probe-Secret", StringComparison.OrdinalIgnoreCase));
    }
}
