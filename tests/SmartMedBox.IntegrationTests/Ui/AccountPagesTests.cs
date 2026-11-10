using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Ui;

/// <summary>UI-01: điều khiển form Razor qua HTTPS như trình duyệt (không có JS).</summary>
public sealed partial class AccountPagesTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Password = "Matkhau123";
    private const string NewPassword = "Matkhaumoi456";

    [Theory]
    [InlineData("/account/register", "Đăng ký tài khoản")]
    [InlineData("/account/verify-email", "Xác minh email")]
    [InlineData("/account/resend-verification", "Gửi lại mã xác minh")]
    [InlineData("/account/login", "Đăng nhập")]
    [InlineData("/account/forgot-password", "Quên mật khẩu")]
    [InlineData("/account/reset-password", "Đặt lại mật khẩu")]
    public async Task Get_AccountPage_ReturnsHtmlWithAntiforgeryForm(string url, string heading)
    {
        using var factory = new TestApplicationFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await ReadHtmlAsync(response);
        Assert.Contains($"<h1>{heading}", html, StringComparison.Ordinal);
        Assert.Matches(AntiforgeryPattern(), html);
    }

    [Fact]
    public async Task Get_AccountLanding_Anonymous_RedirectsToLogin()
    {
        using var factory = new TestApplicationFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync("/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/account/login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("/account/register")]
    [InlineData("/account/verify-email")]
    [InlineData("/account/resend-verification")]
    [InlineData("/account/login")]
    [InlineData("/account/logout")]
    [InlineData("/account/forgot-password")]
    [InlineData("/account/reset-password")]
    public async Task Post_AccountForm_WithoutAntiforgery_IsRejected(string url)
    {
        using var factory = new TestApplicationFactory();
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.PostAsync(
            url,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "a@example.com", ["Password"] = Password }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CSRF_INVALID", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task AccountFlow_ThroughUi_RegisterVerifyLoginForgotResetLogin()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);
        var email = NewEmail("ac12");

        var (register, registerHtml) = await SubmitAsync(client, "/account/register", ("Email", email.ToUpperInvariant()), ("Password", Password));
        Assert.Equal("/account/verify-email", register.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Đăng ký thành công", registerHtml, StringComparison.Ordinal);

        var (unverified, unverifiedHtml) = await SubmitAsync(client, "/account/login", ("Email", email), ("Password", Password));
        Assert.Equal("/account/login", unverified.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Email chưa được xác minh.", unverifiedHtml, StringComparison.Ordinal);

        var verifyToken = ExtractToken(factory.Emails.LastTo(email).Body);
        var (verified, verifiedHtml) = await SubmitAsync(client, $"/account/verify-email?token={verifyToken}", ("Token", verifyToken));
        Assert.Equal("/account/login", verified.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Email đã được xác minh", verifiedHtml, StringComparison.Ordinal);

        var (_, reusedVerifyHtml) = await SubmitAsync(client, "/account/verify-email", ("Token", verifyToken));
        Assert.Contains("Mã không đúng hoặc đã được sử dụng.", reusedVerifyHtml, StringComparison.Ordinal);

        var (loggedIn, landingHtml) = await SubmitAsync(client, "/account/login", ("Email", email), ("Password", Password));
        Assert.Equal("/dashboard", loggedIn.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Tổng quan", landingHtml, StringComparison.Ordinal);

        var (_, forgotHtml) = await SubmitAsync(client, "/account/forgot-password", ("Email", email));
        Assert.Contains("Nếu email tồn tại, hướng dẫn đặt lại đã được gửi.", forgotHtml, StringComparison.Ordinal);
        var resetToken = ExtractToken(factory.Emails.LastTo(email).Body);

        var (_, weakHtml) = await SubmitAsync(client, $"/account/reset-password?token={resetToken}", ("Token", resetToken), ("Password", "short"));
        Assert.Contains("Mật khẩu phải có ít nhất 8 ký tự", weakHtml, StringComparison.Ordinal);

        var (reset, resetHtml) = await SubmitAsync(client, "/account/reset-password", ("Token", resetToken), ("Password", NewPassword));
        Assert.Equal("/account/login", reset.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Mật khẩu đã được đặt lại", resetHtml, StringComparison.Ordinal);

        // Đặt lại mật khẩu thu hồi phiên cũ: trang tài khoản quay về đăng nhập.
        using var afterReset = await client.GetAsync("/account");
        Assert.Equal("/account/login", afterReset.RequestMessage!.RequestUri!.AbsolutePath);

        var (_, reusedResetHtml) = await SubmitAsync(client, "/account/reset-password", ("Token", resetToken), ("Password", "Matkhaukhac789"));
        Assert.Contains("Mã không đúng hoặc đã được sử dụng.", reusedResetHtml, StringComparison.Ordinal);

        var (_, oldPasswordHtml) = await SubmitAsync(client, "/account/login", ("Email", email), ("Password", Password));
        Assert.Contains("Email hoặc mật khẩu không đúng.", oldPasswordHtml, StringComparison.Ordinal);

        var (newLogin, newLandingHtml) = await SubmitAsync(client, "/account/login", ("Email", email), ("Password", NewPassword));
        Assert.Equal("/dashboard", newLogin.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Tổng quan", newLandingHtml, StringComparison.Ordinal);

        var (loggedOut, _) = await SubmitAsync(client, "/account", "/account/logout");
        Assert.Equal("/account/login", loggedOut.RequestMessage!.RequestUri!.AbsolutePath);
        using var afterLogout = await client.GetAsync("/account");
        Assert.Equal("/account/login", afterLogout.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_ShowSameMessage()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);
        var user = await TestUsers.SeedAsync(fixture);

        var (_, wrongPassword) = await SubmitAsync(client, "/account/login", ("Email", user.Email), ("Password", "Saimatkhau999"));
        var (_, unknownEmail) = await SubmitAsync(client, "/account/login", ("Email", NewEmail("missing")), ("Password", "Saimatkhau999"));

        Assert.Equal(ErrorText(wrongPassword), ErrorText(unknownEmail));
        Assert.Contains("Email hoặc mật khẩu không đúng.", wrongPassword, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/account/forgot-password", "Nếu email tồn tại, hướng dẫn đặt lại đã được gửi.")]
    [InlineData("/account/resend-verification", "Nếu email tồn tại và chưa xác minh, chúng tôi đã gửi lại.")]
    public async Task Submit_KnownUnknownOrInvalidEmail_ShowsSameNeutralMessage(string url, string message)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);
        var user = await TestUsers.SeedAsync(fixture);

        var pages = new List<string>();
        foreach (var email in new[] { user.Email, NewEmail("missing"), "" })
        {
            var (response, html) = await SubmitAsync(client, url, ("Email", email));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(message, html, StringComparison.Ordinal);
            pages.Add(AntiforgeryPattern().Replace(html, string.Empty));
        }

        Assert.Single(pages.Distinct());
    }

    [Fact]
    public async Task Register_WeakPasswordOrTakenEmail_ShowsFieldErrors()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);
        var existing = await TestUsers.SeedAsync(fixture);

        var (_, weak) = await SubmitAsync(client, "/account/register", ("Email", NewEmail("weak")), ("Password", "chuachuso"));
        var (_, taken) = await SubmitAsync(client, "/account/register", ("Email", existing.Email.ToUpperInvariant()), ("Password", Password));

        Assert.Contains("Mật khẩu phải có ít nhất 8 ký tự, tối đa 128 ký tự và gồm cả chữ lẫn số.", weak, StringComparison.Ordinal);
        Assert.Contains("Email này đã được đăng ký.", taken, StringComparison.Ordinal);
        Assert.Empty(factory.Emails.Messages);
    }

    [Fact]
    public async Task Login_OverRateLimit_Returns429WithRetryAfter()
    {
        using var baseFactory = new TestApplicationFactory(fixture.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:Login:PermitLimit", "1"));
        using var client = TestUsers.CreateClient(factory);

        await SubmitAsync(client, "/account/login", ("Email", NewEmail("rl")), ("Password", Password));
        var (limited, body) = await SubmitAsync(client, "/account/login", ("Email", NewEmail("rl")), ("Password", Password));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("900", limited.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Bạn đã thử quá nhiều lần", body, StringComparison.Ordinal);
    }

    private static Task<(HttpResponseMessage Response, string Html)> SubmitAsync(
        HttpClient client,
        string url,
        params (string Name, string Value)[] fields) =>
        SubmitAsync(client, url, url.Split('?')[0], fields);

    /// <summary>GET trang chứa form để lấy antiforgery token, rồi POST form như trình duyệt (theo redirect).</summary>
    private static async Task<(HttpResponseMessage Response, string Html)> SubmitAsync(
        HttpClient client,
        string pageUrl,
        string postUrl,
        params (string Name, string Value)[] fields)
    {
        using var page = await client.GetAsync(pageUrl);
        page.EnsureSuccessStatusCode();
        var token = AntiforgeryPattern().Match(await page.Content.ReadAsStringAsync()).Groups[1].Value;

        var form = fields.ToDictionary(f => f.Name, f => f.Value);
        form["__RequestVerificationToken"] = token;
        var response = await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
        return (response, await ReadHtmlAsync(response));
    }

    private static async Task<string> ReadHtmlAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private static string ErrorText(string html) => FormErrorsPattern().Match(html).Value;

    private static string ExtractToken(string emailBody) =>
        emailBody.Split('\n').Select(line => line.Trim()).Single(line => line.Length == 43 && TokenPattern().IsMatch(line));

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    [GeneratedRegex("<div class=\"form-errors[^>]*>.*?</div>", RegexOptions.Singleline)]
    private static partial Regex FormErrorsPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TokenPattern();
}
