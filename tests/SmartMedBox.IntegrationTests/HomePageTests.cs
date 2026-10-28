using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartMedBox.IntegrationTests;

public sealed class HomePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HomePageTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Root_ReturnsHtmlPage()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hộp thuốc thông minh", body, StringComparison.Ordinal);
    }
}
