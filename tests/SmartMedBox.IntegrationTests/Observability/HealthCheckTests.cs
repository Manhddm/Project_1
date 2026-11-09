using System.Diagnostics;
using System.Net;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Observability;

public sealed class HealthCheckTests
{
    // Cổng 1 trên loopback bị từ chối ngay; Connect Timeout ngắn để test không treo.
    private const string UnreachableConnectionString =
        "Server=tcp:127.0.0.1,1;Database=SmartMedBox_Unreachable;User ID=probe;Password=probe-secret-pw;TrustServerCertificate=True;Connect Timeout=2";

    [Fact]
    public async Task GetLive_NoDependencies_ReturnsOkPlainStatus()
    {
        using var factory = new TestApplicationFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetReady_DatabaseUnreachable_ReturnsServiceUnavailableWithoutDetails()
    {
        using var factory = new TestApplicationFactory(UnreachableConnectionString);
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("probe-secret-pw", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30));
    }
}

public sealed class HealthCheckDatabaseTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task GetReady_DatabaseAndStorageAvailable_ReturnsOkPlainStatus()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }
}
