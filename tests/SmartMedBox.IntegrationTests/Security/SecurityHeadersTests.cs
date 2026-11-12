using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartMedBox.Api.Security;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Security;

public sealed class SecurityHeadersTests
{
    [Theory]
    [InlineData("/account/login")] // HTML Razor
    [InlineData("/css/site.css")] // file tĩnh
    [InlineData("/api/users/me")] // API 401 ProblemDetails
    [InlineData("/api/khong-ton-tai")] // 404 qua status code pages
    [InlineData("/health/live")]
    public async Task AnyResponse_HasSecurityHeaders(string url)
    {
        using var factory = new TestApplicationFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync(url);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task UnhandledException_ProblemResponse_StillHasSecurityHeaders()
    {
        using var factory = new TestApplicationFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync("/api/test-probe/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task Production_HttpRequest_RedirectsToHttpsAndHttpsResponseHasHsts()
    {
        using var baseFactory = new TestApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("https_port", "443");
        });
        // HSTS bỏ qua localhost theo mặc định nên dùng host thật.
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://smartmedbox.example"),
            AllowAutoRedirect = false,
        });
        using var https = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://smartmedbox.example"),
        });

        using var redirected = await http.GetAsync("/health/live");
        using var secure = await https.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirected.StatusCode);
        Assert.Equal("https://smartmedbox.example/health/live", redirected.Headers.Location?.ToString());
        Assert.Contains("max-age=", Assert.Single(secure.Headers.GetValues("Strict-Transport-Security")), StringComparison.Ordinal);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Header(response, "Content-Security-Policy"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.TryGetValues(name, out var values)
            ? values
            : response.Content.Headers.GetValues(name));
}
