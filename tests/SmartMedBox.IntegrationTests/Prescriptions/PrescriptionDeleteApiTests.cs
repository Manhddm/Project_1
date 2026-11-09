using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

public sealed class PrescriptionDeleteApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/prescriptions/images";

    [Fact]
    public async Task Delete_Owner_SoftDeletesAndAuditsWhileKeepingFile()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(Jpeg(), "delete-me.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        string storagePath;
        await using (var db = fixture.CreateContext())
        {
            var image = await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == imageId);
            storagePath = GetStoragePath(factory, image.StorageKey);
        }

        Assert.True(File.Exists(storagePath));
        using var response = await owner.DeleteAsync($"{Url}/{imageId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(File.Exists(storagePath));

        using var list = await owner.GetAsync(Url);
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(listBody.RootElement.GetProperty("items").EnumerateArray());

        using var content = await owner.GetAsync($"{Url}/{imageId}/content");
        await AssertCodeAsync(content, HttpStatusCode.NotFound, "IMAGE_NOT_FOUND");

        await using var verify = fixture.CreateContext();
        var row = await verify.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == imageId);
        Assert.True(row.IsDeleted);
        var audit = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(x => x.EntityId == imageId.ToString() && x.Action == AuditActions.PrescriptionImageDeleted);
        Assert.Equal(setup.Owner.Id, audit.ActorUserId);
        Assert.Equal(setup.DeviceId, audit.DeviceId);
    }

    [Fact]
    public async Task Delete_RelativeOrStranger_ReturnsForbidden()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(Jpeg(), "protected.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        foreach (var user in new[] { setup.Relative, setup.Stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, user);
            using var response = await client.DeleteAsync($"{Url}/{imageId}");
            await AssertCodeAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        }
    }

    [Fact]
    public async Task Delete_Repeated_ReturnsImageNotFound()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(Jpeg(), "repeat.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        using var first = await owner.DeleteAsync($"{Url}/{imageId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var second = await owner.DeleteAsync($"{Url}/{imageId}");
        await AssertCodeAsync(second, HttpStatusCode.NotFound, "IMAGE_NOT_FOUND");
    }

    private static MultipartFormDataContent Multipart(byte[] data, string fileName)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static byte[] Jpeg() =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x0B, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01,
        0x00, 0x00, 0x01, 0x00, 0x01,
        0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12, 0x34,
        0xFF, 0xD9,
    ];

    private static string GetStoragePath(TestApplicationFactory factory, string storageKey) =>
        Path.Combine(
            Path.GetFullPath(factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath),
            storageKey);

    private static async Task<HttpClient> LoginWithCsrfAsync(TestApplicationFactory factory, ApplicationUser user)
    {
        var client = await TestUsers.LoginAsync(factory, user);
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
}
