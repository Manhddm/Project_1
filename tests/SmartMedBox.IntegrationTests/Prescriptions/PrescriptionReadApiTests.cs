using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

public sealed class PrescriptionReadApiTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string Url = "/api/prescriptions/images";

    [Fact]
    public async Task List_Owner_ReturnsActiveMetadataWithoutStorageKeyOrPath()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(PrescriptionTestSupport.Jpeg(), "prescription.jpg"));
        upload.EnsureSuccessStatusCode();

        using var response = await owner.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("prescription.jpg", item.GetProperty("originalFileName").GetString());
        Assert.Equal("image/jpeg", item.GetProperty("contentType").GetString());
        Assert.DoesNotContain("storageKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("App_Data", body, StringComparison.OrdinalIgnoreCase);
        // Không dò dấu "/" vì "image/jpeg" luôn chứa nó: so với storage key thật.
        await using var db = fixture.CreateContext();
        var storageKey = (await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == item.GetProperty("id").GetGuid())).StorageKey;
        Assert.DoesNotContain(storageKey, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_Owner_ReturnsStoredBytesAndPrivateSecurityHeaders()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(PrescriptionTestSupport.Jpeg(), "prescription.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        byte[] stored;
        string storageKey;
        await using (var db = fixture.CreateContext())
        {
            var image = await db.PrescriptionImages.AsNoTracking().SingleAsync(x => x.Id == imageId);
            storageKey = image.StorageKey;
            stored = await File.ReadAllBytesAsync(GetStoragePath(factory, storageKey));
        }

        using var response = await owner.GetAsync($"{Url}/{imageId}/content");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("prescription.jpg", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(stored, await response.Content.ReadAsByteArrayAsync());

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(storageKey, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain("App_Data", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAndDownload_RelativeStrangerAndAnonymous_ReturnExpectedAuthorization()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var upload = await owner.PostAsync(Url, Multipart(PrescriptionTestSupport.Jpeg(), "prescription.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        foreach (var user in new[] { setup.Relative, setup.Stranger })
        {
            using var client = await LoginWithCsrfAsync(factory, user);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/{imageId}/content")).StatusCode);
        }

        using var anonymous = TestUsers.CreateClient(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Url}/{imageId}/content")).StatusCode);
    }

    [Fact]
    public async Task Download_OtherOwnersImage_ReturnsImageNotFound()
    {
        var setup = await PrescriptionTestSupport.SeedAsync(fixture);
        var otherSetup = await PrescriptionTestSupport.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var otherOwner = await LoginWithCsrfAsync(factory, otherSetup.Owner);
        using var upload = await otherOwner.PostAsync(Url, Multipart(PrescriptionTestSupport.Jpeg(), "other.jpg"));
        upload.EnsureSuccessStatusCode();
        using var uploadBody = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
        var imageId = uploadBody.RootElement.GetProperty("id").GetGuid();

        using var owner = await LoginWithCsrfAsync(factory, setup.Owner);
        using var response = await owner.GetAsync($"{Url}/{imageId}/content");
        await AssertCodeAsync(response, HttpStatusCode.NotFound, "IMAGE_NOT_FOUND");
    }

    private static MultipartFormDataContent Multipart(byte[] data, string fileName)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

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
