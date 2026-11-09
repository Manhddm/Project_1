using System.Net;
using System.Text.Json;
using SmartMedBox.IntegrationTests.Prescriptions;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Ui;

public sealed class PrescriptionPagesTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task Get_OwnerPage_ReturnsOwnerOnlyRazorUiWithAntiforgery()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await TestUsers.LoginAsync(factory, setup.Owner);

        using var response = await client.GetAsync("/prescriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ảnh đơn thuốc", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.Contains("/js/prescriptions.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("storageKey", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceApiKey", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_RelativePage_ReturnsForbiddenProblemDetails()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await TestUsers.LoginAsync(factory, setup.Relative);

        using var response = await client.GetAsync("/prescriptions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FORBIDDEN", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Get_AnonymousPage_RedirectsToLogin()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.GetAsync("/prescriptions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/account/login", response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
