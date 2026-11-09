using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Auth;

public sealed class AuthApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Password = "P@ssw0rd!2026";

    [Fact]
    public async Task Login_WithoutCsrfToken_ReturnsCsrfInvalid()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@example.com", password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CSRF_INVALID", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ReturnSameInvalidCredentials()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedUserAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        using var wrongPassword = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = "wrong-password" },
            csrfToken);
        using var unknownEmail = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = "unknown@example.com", password = Password },
            csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ReadCodeAsync(wrongPassword));
        Assert.Equal("INVALID_CREDENTIALS", await ReadCodeAsync(unknownEmail));
        Assert.Equal(await ReadDetailAsync(wrongPassword), await ReadDetailAsync(unknownEmail));
    }

    [Fact]
    public async Task Login_UnverifiedEmail_ReturnsEmailNotVerified()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedUserAsync(emailVerified: false);
        var csrfToken = await GetCsrfTokenAsync(client);

        using var response = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = Password },
            csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("EMAIL_NOT_VERIFIED", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Login_VerifiedUser_SetsSecureCookieAndMeReturnsProfile()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedUserAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = Password },
            csrfToken);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authCookie = GetCookie(login, "SmartMedBox.Auth");
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);

        using var me = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(user.Id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal(user.Email, root.GetProperty("email").GetString());
        Assert.True(root.GetProperty("emailVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ownedDevice").ValueKind);
    }

    [Fact]
    public async Task Logout_RevokesServerSession_AndIsIdempotent()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedUserAsync();
        var csrfToken = await GetCsrfTokenAsync(client);

        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = Password },
            csrfToken);
        login.EnsureSuccessStatusCode();
        var authCookiePair = GetCookie(login, "SmartMedBox.Auth").Split(';', 2)[0];

        var logoutToken = await GetCsrfTokenAsync(client);
        using var logout = await PostJsonWithCsrfAsync(client, "/api/auth/logout", new { }, logoutToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var staleClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        staleClient.DefaultRequestHeaders.Add("Cookie", authCookiePair);
        using var staleMe = await staleClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

        var anonymousToken = await GetCsrfTokenAsync(client);
        using var secondLogout = await PostJsonWithCsrfAsync(client, "/api/auth/logout", new { }, anonymousToken);
        Assert.Equal(HttpStatusCode.NoContent, secondLogout.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutLogin_ReturnsUnauthorized()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ReadCodeAsync(response));
    }

    private static HttpClient CreateClient(TestApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/test-probe/csrf");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> PostJsonWithCsrfAsync(
        HttpClient client,
        string url,
        object body,
        string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return client.SendAsync(request);
    }

    private static string GetCookie(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith($"{name}=", StringComparison.Ordinal));

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString();
    }

    private async Task<ApplicationUser> SeedUserAsync(bool emailVerified = true)
    {
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = new PasswordHasherService().Hash(Password),
            EmailVerified = emailVerified,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
