using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Auth;

public sealed class RateLimitingTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task Login_EleventhAttemptWithin15Minutes_Returns429WithRetryAfter()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);

        // Mọi lần thử đều bị đếm, kể cả lần bị từ chối vì thiếu CSRF/sai mật khẩu.
        for (var i = 0; i < 10; i++)
        {
            using var allowed = await PostLoginAsync(client);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var limited = await PostLoginAsync(client);

        await AssertRateLimitedAsync(limited, windowSeconds: 15 * 60);
    }

    [Fact]
    public async Task Login_Limited_DoesNotAffectOtherEndpoints()
    {
        using var factory = WithSettings(("RateLimiting:Login:PermitLimit", "1"));
        using var client = TestUsers.CreateClient(factory);
        using var first = await PostLoginAsync(client);
        using var limited = await PostLoginAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        using var me = await client.GetAsync("/api/users/me");
        using var csrf = await client.GetAsync("/api/test-probe/csrf");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
    }

    [Fact]
    public async Task Pair_ConfiguredThreshold_IsApplied()
    {
        using var factory = WithSettings(("RateLimiting:Pair:PermitLimit", "2"), ("RateLimiting:Pair:WindowSeconds", "60"));
        using var client = TestUsers.CreateClient(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/test-probe/rate-limit/pair", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/test-probe/rate-limit/pair", null)).StatusCode);
        using var limited = await client.PostAsync("/api/test-probe/rate-limit/pair", null);

        await AssertRateLimitedAsync(limited, windowSeconds: 60);
    }

    [Fact]
    public async Task InvitationCreate_CountsPerUser_NotPerIp()
    {
        using var factory = WithSettings(("RateLimiting:InvitationCreate:PermitLimit", "1"));
        using var userA = await TestUsers.LoginAsync(factory, await TestUsers.SeedAsync(fixture));
        using var userB = await TestUsers.LoginAsync(factory, await TestUsers.SeedAsync(fixture));

        Assert.Equal(HttpStatusCode.NoContent, (await userA.PostAsync("/api/test-probe/rate-limit/invitation", null)).StatusCode);
        using var limitedA = await userA.PostAsync("/api/test-probe/rate-limit/invitation", null);
        using var allowedB = await userB.PostAsync("/api/test-probe/rate-limit/invitation", null);

        await AssertRateLimitedAsync(limitedA, windowSeconds: 24 * 60 * 60);
        Assert.Equal(HttpStatusCode.NoContent, allowedB.StatusCode);
    }

    [Fact]
    public void Startup_InvalidThreshold_FailsFast()
    {
        using var factory = WithSettings(("RateLimiting:Login:PermitLimit", "0"));

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("RateLimiting", error.ToString(), StringComparison.Ordinal);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    private static Task<HttpResponseMessage> PostLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "wrong-password-1" });

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, int windowSeconds)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")));
        Assert.Equal(windowSeconds, retryAfter);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }
}
