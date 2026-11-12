using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.IntegrationTests.TestSupport;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    // Test không truy cập DB dùng connection string giả; test cần DB truyền connection string của fixture.
    private const string DummyConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=SmartMedBox_Unused;Trusted_Connection=True;TrustServerCertificate=True";

    private const string TestTokenPepper = "integration-test-pepper-0123456789abcdef0123456789abcdef";

    private readonly string _connectionString;

    public TestApplicationFactory()
        : this(null)
    {
    }

    internal TestApplicationFactory(string? connectionString)
    {
        _connectionString = connectionString ?? DummyConnectionString;
    }

    public RecordingEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Security:TokenPepper", TestTokenPepper);
        // Job nền quét toàn bảng; test gọi service trực tiếp. Test riêng bật lại qua WithWebHostBuilder.
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddControllers().AddApplicationPart(typeof(TestProbeController).Assembly);
        });
    }
}
