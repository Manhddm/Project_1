using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Auth;

public sealed partial class PasswordResetApiTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Password = "Matkhau123";
    private const string NewPassword = "Matkhaumoi456";

    [Fact]
    public async Task ForgotPassword_UnknownEmail_ReturnsAcceptedAndSendsNothing()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = NewEmail("missing") });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.Messages);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task ForgotPassword_InvalidEmail_ReturnsAcceptedAndSendsNothing(string email)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task ForgotPassword_ExistingUser_SendsPasswordResetToken()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedVerifiedUserAsync();

        var token = await RequestResetTokenAsync(client, factory, user.Email);

        Assert.Equal(43, token.Length);
        await using var db = fixture.CreateContext();
        var stored = await db.AuthTokens.SingleAsync(x => x.UserId == user.Id);
        Assert.Equal(AuthTokenPurpose.PasswordReset, stored.Purpose);
        Assert.Null(stored.UsedAt);
        Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        Assert.DoesNotContain(token, stored.TokenHash);
    }

    [Fact]
    public async Task ForgotPassword_SecondRequest_InvalidatesPreviousToken()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedVerifiedUserAsync();
        var firstToken = await RequestResetTokenAsync(client, factory, user.Email);

        using var second = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = user.Email });
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        var secondToken = ExtractToken(factory.Emails.LastTo(user.Email).Body);
        Assert.NotEqual(firstToken, secondToken);

        using var oldToken = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = firstToken, newPassword = NewPassword });
        using var latestToken = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = secondToken, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, oldToken.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(oldToken));
        Assert.Equal(HttpStatusCode.OK, latestToken.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ChangesPasswordAndRevokesSessions()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedVerifiedUserAsync();

        var csrfToken = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = Password },
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var staleAuthCookie = GetCookie(login, "SmartMedBox.Auth").Split(';', 2)[0];

        var token = await RequestResetTokenAsync(client, factory, user.Email);
        using var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var staleClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        staleClient.DefaultRequestHeaders.Add("Cookie", staleAuthCookie);
        using var staleMe = await staleClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

        using var reuse = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(reuse));

        var freshCsrfToken = await GetCsrfTokenAsync(client);
        using var oldPasswordLogin = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = Password },
            freshCsrfToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await ReadCodeAsync(oldPasswordLogin));

        using var newPasswordLogin = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email = user.Email, password = NewPassword },
            freshCsrfToken);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        await using var db = fixture.CreateContext();
        var storedUser = await db.ApplicationUsers.SingleAsync(x => x.Id == user.Id);
        Assert.True(new PasswordHasherService().Verify(storedUser.PasswordHash, NewPassword));
        var storedToken = await db.AuthTokens.SingleAsync(x => x.UserId == user.Id);
        Assert.NotNull(storedToken.UsedAt);
    }

    [Fact]
    public async Task ResetPassword_WeakPassword_ReturnsValidationError_AndTokenRemainsUsable()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedVerifiedUserAsync();
        var token = await RequestResetTokenAsync(client, factory, user.Email);

        using var weak = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token, newPassword = "short1" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadCodeAsync(weak));
        using (var body = JsonDocument.Parse(await weak.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("password", out _));
        }

        using var retry = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UnknownToken_ReturnsTokenInvalid()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = "unknown-token-value", newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task ResetPassword_ExpiredToken_ReturnsTokenExpired()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var user = await SeedVerifiedUserAsync();
        var token = await RequestResetTokenAsync(client, factory, user.Email);

        await using (var db = fixture.CreateContext())
        {
            await db.AuthTokens
                .Where(x => x.UserId == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.ExpiresAt,
                    DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        using var response = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("TOKEN_EXPIRED", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task AccountFlow_RegisterVerifyLoginForgotReset_EndToEnd()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("flow");

        using var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var verificationToken = ExtractToken(factory.Emails.SingleTo(email).Body);
        using var verify = await client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var csrfToken = await GetCsrfTokenAsync(client);
        using var login = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email, password = Password },
            csrfToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var resetToken = ExtractToken(factory.Emails.LastTo(email).Body);

        using var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = resetToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var freshCsrfToken = await GetCsrfTokenAsync(client);
        using var newPasswordLogin = await PostJsonWithCsrfAsync(
            client,
            "/api/auth/login",
            new { email, password = NewPassword },
            freshCsrfToken);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }

    private static HttpClient CreateClient(TestApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static string ExtractToken(string emailBody) =>
        emailBody
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => TokenPattern().IsMatch(line) && line.Length == 43);

    private static async Task<string> RequestResetTokenAsync(
        HttpClient client,
        TestApplicationFactory factory,
        string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email });
        response.EnsureSuccessStatusCode();

        return ExtractToken(factory.Emails.LastTo(email).Body);
    }

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

    private async Task<ApplicationUser> SeedVerifiedUserAsync()
    {
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = NewEmail("reset"),
            PasswordHash = new PasswordHasherService().Hash(Password),
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenPattern();
}
