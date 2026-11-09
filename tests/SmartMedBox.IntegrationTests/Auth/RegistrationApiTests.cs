using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Enums;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Auth;

public sealed partial class RegistrationApiTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Password = "Matkhau123";

    [Fact]
    public async Task Register_ValidRequest_CreatesUnverifiedUserAndSendsVerificationEmail()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("register");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.True(body.RootElement.GetProperty("emailVerificationRequired").GetBoolean());

        await using (var db = fixture.CreateContext())
        {
            var user = await db.ApplicationUsers.SingleAsync(x => x.Email == email);
            Assert.False(user.EmailVerified);
            var token = await db.AuthTokens.SingleAsync(x => x.UserId == user.Id);
            Assert.Equal(AuthTokenPurpose.EmailVerification, token.Purpose);
            Assert.Null(token.UsedAt);
            Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        }

        var message = factory.Emails.SingleTo(email);
        var verificationToken = ExtractToken(message.Body);
        Assert.DoesNotContain(Password, message.Body);

        using var verify = await client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token = verificationToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        using var verifyBody = JsonDocument.Parse(await verify.Content.ReadAsStringAsync());
        Assert.True(verifyBody.RootElement.GetProperty("emailVerified").GetBoolean());

        await using (var db = fixture.CreateContext())
        {
            var user = await db.ApplicationUsers.SingleAsync(x => x.Email == email);
            Assert.True(user.EmailVerified);
            var token = await db.AuthTokens.SingleAsync(x => x.UserId == user.Id);
            Assert.NotNull(token.UsedAt);
        }
    }

    [Fact]
    public async Task Register_DuplicateEmailWithDifferentCase_ReturnsEmailTaken()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("duplicate");

        using var first = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password });
        using var second = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = email.ToUpperInvariant(), password = Password });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("EMAIL_TAKEN", await ReadCodeAsync(second));

        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.ApplicationUsers.CountAsync(x => x.Email == email));
    }

    [Theory]
    [InlineData("Mat1")]            // quá ngắn
    [InlineData("matkhaudai")]      // không có số
    [InlineData("123456789")]       // không có chữ
    public async Task Register_WeakPassword_ReturnsValidationError(string weakPassword)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("weak");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = weakPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadCodeAsync(response));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("password", out _));

        await using var db = fixture.CreateContext();
        Assert.False(await db.ApplicationUsers.AnyAsync(x => x.Email == email));
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsValidationError()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "not-an-email", password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", await ReadCodeAsync(response));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task VerifyEmail_ReusedToken_ReturnsTokenInvalid()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("reuse");
        var token = await RegisterAndGetTokenAsync(client, factory, email);

        using var first = await client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        using var second = await client.PostAsJsonAsync("/api/auth/verify-email", new { token });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(second));
    }

    [Fact]
    public async Task VerifyEmail_UnknownToken_ReturnsTokenInvalid()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token = "unknown-token-value" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task VerifyEmail_ExpiredToken_ReturnsTokenExpired()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("expired");
        var token = await RegisterAndGetTokenAsync(client, factory, email);

        await using (var db = fixture.CreateContext())
        {
            var userId = await db.ApplicationUsers
                .Where(x => x.Email == email)
                .Select(x => x.Id)
                .SingleAsync();
            await db.AuthTokens
                .Where(x => x.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        using var response = await client.PostAsJsonAsync("/api/auth/verify-email", new { token });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("TOKEN_EXPIRED", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task ResendVerification_UnknownEmail_ReturnsAcceptedAndSendsNothing()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new { email = NewEmail("missing") });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task ResendVerification_ExistingUnverifiedUser_InvalidatesOldToken()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("resend");
        var oldToken = await RegisterAndGetTokenAsync(client, factory, email);

        using var resend = await client.PostAsJsonAsync(
            "/api/auth/resend-verification",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        var newToken = ExtractToken(factory.Emails.LastTo(email).Body);
        Assert.NotEqual(oldToken, newToken);

        using var oldTokenResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new { token = oldToken });
        using var newTokenResponse = await client.PostAsJsonAsync("/api/auth/verify-email", new { token = newToken });

        Assert.Equal(HttpStatusCode.BadRequest, oldTokenResponse.StatusCode);
        Assert.Equal("TOKEN_INVALID", await ReadCodeAsync(oldTokenResponse));
        Assert.Equal(HttpStatusCode.OK, newTokenResponse.StatusCode);
    }

    [Fact]
    public async Task Register_Response_DoesNotExposePasswordOrToken()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = CreateClient(factory);
        var email = NewEmail("noleak");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password });
        var rawBody = await response.Content.ReadAsStringAsync();

        var message = factory.Emails.SingleTo(email);
        var token = ExtractToken(message.Body);

        Assert.DoesNotContain(Password, rawBody);
        Assert.DoesNotContain(token, rawBody);
        Assert.DoesNotContain("passwordHash", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient(TestApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static async Task<string> RegisterAndGetTokenAsync(
        HttpClient client,
        TestApplicationFactory factory,
        string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = Password });
        response.EnsureSuccessStatusCode();

        return ExtractToken(factory.Emails.SingleTo(email).Body);
    }

    private static string ExtractToken(string emailBody) =>
        emailBody
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => TokenPattern().IsMatch(line) && line.Length == 43);

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenPattern();
}