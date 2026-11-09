using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Prescriptions;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.Infrastructure.Time;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

public sealed class PrescriptionUploadApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/prescriptions/images";
    private const string Gps = "GPS 21.0285N 105.8542E";

    [Fact]
    public async Task Upload_Owner_Returns201_StoresCleanFileOutsideWebRootAndMetadataOnly()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        using var response = await owner.PostAsync(Url, Multipart(JpegWithGps(), "don-thuoc.jpg", "application/octet-stream"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal("don-thuoc.jpg", root.GetProperty("originalFileName").GetString());
        Assert.Equal("image/jpeg", root.GetProperty("contentType").GetString()); // theo magic bytes, không theo Content-Type
        Assert.True(root.TryGetProperty("uploadedAt", out _));

        await using var db = fixture.CreateContext();
        var row = await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(setup.Owner.Id, row.OwnerUserId);
        Assert.Matches("^[a-f0-9]{32}\\.jpg$", row.StorageKey);
        Assert.Equal(root.GetProperty("sizeBytes").GetInt32(), row.SizeBytes);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(x => x.EntityId == id.ToString());
        Assert.Equal(AuditActions.PrescriptionImageUploaded, audit.Action);
        Assert.Equal(setup.DeviceId, audit.DeviceId);
        Assert.DoesNotContain("don-thuoc", audit.Details);

        var rootPath = Path.GetFullPath(factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath);
        var webRoot = Path.GetFullPath(factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath);
        var path = Path.Combine(rootPath, row.StorageKey);
        try
        {
            Assert.False(path.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var stored = await File.ReadAllBytesAsync(path);
            Assert.Equal(row.SizeBytes, stored.Length);
            Assert.Equal([0xFF, 0xD8, 0xFF], stored[..3]);
            Assert.True(stored.AsSpan().IndexOf("Exif"u8) < 0);
            Assert.True(stored.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Gps)) < 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Upload_RelativeOrStranger_Returns403()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);

        foreach (var user in new[] { setup.Relative, setup.Stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, user);
            await AssertCodeAsync(await client.PostAsync(Url, Multipart(JpegWithGps(), "a.jpg")), HttpStatusCode.Forbidden, "FORBIDDEN");
        }

        await using var db = fixture.CreateContext();
        Assert.False(await db.PrescriptionImages.AnyAsync(x => x.OwnerUserId == setup.Relative.Id || x.OwnerUserId == setup.Stranger.Id));
    }

    [Fact]
    public async Task Upload_Anonymous_Returns401()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = TestUsers.CreateClient(factory);
        using var csrf = JsonDocument.Parse(await client.GetStringAsync("/api/test-probe/csrf"));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());

        await AssertCodeAsync(await client.PostAsync(Url, Multipart(JpegWithGps(), "a.jpg")), HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task Upload_ExeRenamedJpg_Returns415()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        byte[] exe = [.. "MZ\x90\0\x03\0\0\0\x04\0\0\0\xFF\xFF"u8, .. new byte[200]];

        await AssertCodeAsync(await owner.PostAsync(Url, Multipart(exe, "don-thuoc.jpg", "image/jpeg")),
            HttpStatusCode.UnsupportedMediaType, "FILE_TYPE_NOT_ALLOWED");
        await AssertNoImagesAsync(setup.Owner.Id);
    }

    [Fact]
    public async Task Upload_CorruptJpeg_Returns400ImageInvalid()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        await AssertCodeAsync(await owner.PostAsync(Url, Multipart(JpegWithGps()[..30], "a.jpg")),
            HttpStatusCode.BadRequest, "IMAGE_INVALID");
        await AssertNoImagesAsync(setup.Owner.Id);
    }

    [Theory]
    [InlineData(PrescriptionImageService.MaxBytes + 1)]
    [InlineData(PrescriptionImageService.MaxBytes + 1024 * 1024)]
    public async Task Upload_OverFiveMegabytes_Returns413(int size)
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        await AssertCodeAsync(await owner.PostAsync(Url, Multipart(PaddedJpeg(size), "big.jpg")),
            HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");
        await AssertNoImagesAsync(setup.Owner.Id);
    }

    [Fact]
    public async Task Upload_ExactlyFiveMegabytes_Returns201()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);

        using var response = await owner.PostAsync(Url, Multipart(PaddedJpeg(PrescriptionImageService.MaxBytes), "max.jpg"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = fixture.CreateContext();
        var row = await db.PrescriptionImages.SingleAsync(x => x.OwnerUserId == setup.Owner.Id);
        await factory.Services.GetRequiredService<IFileStorage>().DeleteAsync(row.StorageKey);
    }

    [Fact]
    public async Task UploadAsync_DatabaseFailure_LeavesNoOrphanFile()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        var dir = Path.Combine(Path.GetTempPath(), "smb-orphan-" + Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = dir }));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new FailingSaveInterceptor())
            .Options);
        var service = new PrescriptionImageService(db, storage, new SystemClock());

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.UploadAsync(setup.Owner.Id, "a.jpg", new MemoryStream(JpegWithGps())));
            Assert.Empty(Directory.Exists(dir) ? Directory.GetFiles(dir) : []);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        await AssertNoImagesAsync(setup.Owner.Id);
    }

    private async Task AssertNoImagesAsync(Guid ownerId)
    {
        await using var db = fixture.CreateContext();
        Assert.False(await db.PrescriptionImages.AnyAsync(x => x.OwnerUserId == ownerId));
    }

    private static MultipartFormDataContent Multipart(byte[] data, string fileName, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    // JPEG tối giản: JFIF + EXIF có GPS + bảng/khung/scan giả + EOI.
    private static byte[] JpegWithGps() =>
    [
        0xFF, 0xD8,
        .. Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]),
        .. Segment(0xE1, [.. "Exif\0\0"u8, .. Encoding.ASCII.GetBytes(Gps)]),
        .. Segment(0xDB, new byte[65]),
        .. Segment(0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]),
        .. Segment(0xDA, [1, 1, 0, 0, 0x3F, 0]),
        0x12, 0x34,
        0xFF, 0xD9,
    ];

    // JPEG hợp lệ cấu trúc có kích thước đúng bằng size (đệm bằng các segment COM).
    private static byte[] PaddedJpeg(int size)
    {
        var head = JpegWithGps()[..^2];
        var result = new byte[size];
        head.CopyTo(result, 0);
        var pos = head.Length;
        var remaining = size - 2 - pos;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, 65537); // 4 byte marker+length + tối đa 65533 byte payload
            if (remaining - chunk is > 0 and < 4)
            {
                chunk -= 4;
            }

            result[pos] = 0xFF;
            result[pos + 1] = 0xFE;
            result[pos + 2] = (byte)((chunk - 2) >> 8);
            result[pos + 3] = (byte)(chunk - 2);
            pos += chunk;
            remaining -= chunk;
        }

        result[size - 2] = 0xFF;
        result[size - 1] = 0xD9;
        return result;
    }

    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    private static async Task<HttpClient> LoginWithCsrfAsync(TestApplicationFactory factory, ApplicationUser user)
    {
        var client = await TestUsers.LoginAsync(factory, user);
        // Token antiforgery gắn với danh tính: phải lấy lại sau khi đăng nhập.
        using var csrf = JsonDocument.Parse(await client.GetStringAsync("/api/test-probe/csrf"));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        return client;
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("simulated failure");
    }
}
