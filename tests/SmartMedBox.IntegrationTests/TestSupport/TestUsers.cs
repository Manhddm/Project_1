using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.IntegrationTests.Schema;

namespace SmartMedBox.IntegrationTests.TestSupport;

public static class TestUsers
{
    public const string Password = "P@ssw0rd!2026";

    public static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    public static async Task<ApplicationUser> SeedAsync(SqlServerDatabaseFixture fixture)
    {
        await using var db = fixture.CreateContext();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = $"user-{Guid.NewGuid():N}@example.com",
            PasswordHash = new PasswordHasherService().Hash(Password),
            EmailVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ApplicationUsers.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, ApplicationUser user)
    {
        var client = CreateClient(factory);
        using var csrf = await client.GetAsync("/api/test-probe/csrf");
        csrf.EnsureSuccessStatusCode();
        using var csrfBody = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = user.Email, password = Password }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfBody.RootElement.GetProperty("token").GetString());
        using var login = await client.SendAsync(request);
        login.EnsureSuccessStatusCode();
        return client;
    }
}
