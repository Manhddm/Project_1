using System.Net;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Ui;

public sealed class RelativeAlertPagesTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Theory]
    [InlineData("/relatives", "Người thân", "/js/relatives.js")]
    [InlineData("/alerts", "Cảnh báo", "/js/alerts.js")]
    public async Task Get_LoggedInPage_RendersAntiforgeryAndScript(string url, string heading, string script)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await TestUsers.LoginAsync(factory, await TestUsers.SeedAsync(fixture));

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<h1>{heading}</h1>", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains(script, html, StringComparison.Ordinal);
        Assert.Contains("href=\"/relatives\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/alerts\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("đã uống", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/relatives")]
    [InlineData("/alerts")]
    public async Task Get_AnonymousPage_RedirectsToLogin(string url)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/account/login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
