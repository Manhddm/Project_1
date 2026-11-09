using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Prescriptions;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

public sealed class PrescriptionStorageTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task PrescriptionImages_Schema_HasNoBinaryColumns()
    {
        await using var db = fixture.CreateContext();
        var types = await db.Database
            .SqlQueryRaw<string>(
                "SELECT DATA_TYPE AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PrescriptionImages'")
            .ToListAsync();

        Assert.NotEmpty(types);
        Assert.DoesNotContain(types, t => t is "varbinary" or "binary" or "image");
    }

    [Fact]
    public async Task StorageRoot_IsOutsideWebRoot_AndNotServedStatically()
    {
        using var factory = new TestApplicationFactory();
        var root = Path.GetFullPath(factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath);
        var webRoot = Path.GetFullPath(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);

        Assert.False(
            root.StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(root, webRoot, StringComparison.OrdinalIgnoreCase));

        var storage = factory.Services.GetRequiredService<IFileStorage>();
        var key = await storage.SaveAsync(new MemoryStream([1, 2, 3]), "jpg");
        try
        {
            using var client = TestUsers.CreateClient(factory);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{key}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/App_Data/uploads/{key}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/uploads/{key}")).StatusCode);
        }
        finally
        {
            await storage.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task GetOwnedDeviceIdAsync_OnlyOwnerGetsDevice()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        await using var db = fixture.CreateContext();
        var service = new PrescriptionImageService(
            db,
            new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = Path.GetTempPath() })),
            new SmartMedBox.Infrastructure.Time.SystemClock());

        Assert.Equal(setup.DeviceId, await service.GetOwnedDeviceIdAsync(setup.Owner.Id));
        Assert.Null(await service.GetOwnedDeviceIdAsync(setup.Relative.Id));
        Assert.Null(await service.GetOwnedDeviceIdAsync(setup.Stranger.Id));
    }
}
