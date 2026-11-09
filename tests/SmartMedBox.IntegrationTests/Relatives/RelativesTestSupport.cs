using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Relatives;

internal static partial class RelativesTestSupport
{
    public static async Task<HttpClient> LoginWithCsrfAsync(WebApplicationFactory<Program> factory, ApplicationUser user)
    {
        var client = await TestUsers.LoginAsync(factory, user);
        await AddCsrfAsync(client);
        return client;
    }

    public static async Task AddCsrfAsync(HttpClient client)
    {
        // Token antiforgery gắn với danh tính hiện tại, nên lấy lại sau mỗi lần đăng nhập.
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var csrf = await client.GetAsync("/api/test-probe/csrf");
        csrf.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.RootElement.GetProperty("token").GetString());
    }

    public static async Task<Guid> SeedDeviceAsync(SqlServerDatabaseFixture fixture, Guid? ownerUserId)
    {
        await using var db = fixture.CreateContext();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceId = $"esp32-{Guid.NewGuid():N}",
            SerialNumber = $"SN-{Guid.NewGuid():N}",
            DeviceApiKeyHash = Guid.NewGuid().ToString("N"),
            OwnerUserId = ownerUserId,
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return device.Id;
    }

    public static async Task<Guid> SeedRelationshipAsync(
        SqlServerDatabaseFixture fixture,
        Guid deviceId,
        Guid relativeUserId,
        RelationshipStatus status = RelationshipStatus.Active)
    {
        await using var db = fixture.CreateContext();
        var relationship = new CareRelationship
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            RelativeUserId = relativeUserId,
            Status = status,
            AcceptedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = status == RelationshipStatus.Revoked ? DateTimeOffset.UtcNow : null,
        };
        db.CareRelationships.Add(relationship);
        await db.SaveChangesAsync();
        return relationship.Id;
    }

    public static Task<HttpResponseMessage> InviteAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/relatives/invitations", new { email });

    public static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/relatives/invitations/accept", new { token });

    public static string ExtractToken(string emailBody) =>
        emailBody.Split('\n').Select(line => line.Trim()).Single(line => TokenPattern().IsMatch(line));

    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenPattern();
}
