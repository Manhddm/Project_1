using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests;

public sealed class HomePageTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public HomePageTests(TestApplicationFactory factory)
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
