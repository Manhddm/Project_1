using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Security;

public sealed class LogRedactionTests
{
    [Fact]
    public async Task Requests_WithDeviceKeyCookieAndBody_NeverAppearInLogs()
    {
        var logs = new CapturingLoggerProvider();
        using var baseFactory = new TestApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(logs))));
        using var client = factory.CreateClient();

        using var device = new HttpRequestMessage(HttpMethod.Post, "/api/device/heartbeat");
        device.Headers.Add("X-Device-Id", "device-id-secret-123");
        device.Headers.Add("X-Device-Api-Key", "device-key-secret-456");
        using var deviceResponse = await client.SendAsync(device);

        using var withCookie = new HttpRequestMessage(HttpMethod.Post, "/api/test-probe/validate")
        {
            Content = new StringContent("{\"name\":\"body-secret-789\"}", Encoding.UTF8, "application/json"),
        };
        withCookie.Headers.Add("Cookie", "SmartMedBox.Auth=cookie-secret-abc; SmartMedBox.Csrf=csrf-secret-def");
        using var cookieResponse = await client.SendAsync(withCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, deviceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cookieResponse.StatusCode);
        Assert.Contains(logs.Logs, log => log.Category.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal));
        foreach (var secret in new[] { "device-id-secret-123", "device-key-secret-456", "X-Device-Api-Key", "body-secret-789", "cookie-secret-abc", "csrf-secret-def" })
        {
            Assert.DoesNotContain(logs.Logs, log => log.AllText.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }
}
