using System.Net;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Ui;

public sealed class AppPagesTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    // Thư mục view phải khớp tên controller (*Pages), nếu không trang trả 500 "view not found".
    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/devices")]
    [InlineData("/devices/pair")]
    [InlineData("/schedules")]
    public async Task Get_LoggedInPage_RendersView(string url)
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await TestUsers.LoginAsync(factory, await TestUsers.SeedAsync(fixture));

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(url, response.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
