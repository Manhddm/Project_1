using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Alerts;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.Infrastructure.Schedules;
using SmartMedBox.IntegrationTests.Alerts;
using SmartMedBox.IntegrationTests.Relatives;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.IntegrationTests.Acceptance;

internal sealed record AcceptanceUser(Guid Id, string Email, HttpClient Client);

/// <summary>
/// P7-02a: the real app (WebApplicationFactory) on a fresh SQL Server DB, a fake clock and a recording/failing email sink.
/// Background jobs stay disabled (TestApplicationFactory); tests run the same scoped services the jobs call, deterministically.
/// Users go through the real HTTP API (register → verify → login + CSRF, pair, invite/accept).
/// </summary>
internal sealed class AcceptanceHarness : IAsyncDisposable
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh"; // UTC+7, no DST
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    private readonly SqlServerDatabaseFixture _database = new();
    private readonly List<HttpClient> _clients = [];

    private AcceptanceHarness()
    {
        // The cookie middleware checks expiry against real time, so the scenario day lies in the future (never in the past).
        Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        Clock = new FakeClock(At(6, 0));
    }

    /// <summary>Scenario day (local date in <see cref="TimeZoneId"/>); schedules start on it.</summary>
    public DateOnly Day { get; }

    public FakeClock Clock { get; }

    /// <summary>Records every successful send; <c>FailFor</c> simulates a failing address.</summary>
    public FakeEmailSender Emails { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public static async Task<AcceptanceHarness> StartAsync()
    {
        var harness = new AcceptanceHarness();
        await harness._database.InitializeAsync();
        harness.Factory = new TestApplicationFactory(harness._database.ConnectionString).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(harness.Clock);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(harness.Emails);
            }));
        return harness;
    }

    /// <summary>Local wall-clock time on the scenario day (+ <paramref name="dayOffset"/>) as a UTC instant.</summary>
    public DateTimeOffset At(int hour, int minute, int second = 0, int dayOffset = 0) =>
        new(TimeZoneInfo.ConvertTimeToUtc(Day.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute, second)), Zone), TimeSpan.Zero);

    public void SetTime(DateTimeOffset utc) => Clock.UtcNow = utc;

    public AppDbContext CreateContext() => _database.CreateContext();

    public HttpClient CreateClient()
    {
        var client = TestUsers.CreateClient(Factory);
        _clients.Add(client);
        return client;
    }

    // ---- Users (real HTTP API) ----

    public async Task<AcceptanceUser> RegisterAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var client = CreateClient();
        using (var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = TestUsers.Password }))
        {
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        }

        using (var verify = await client.PostAsJsonAsync("/api/auth/verify-email", new { token = LastTokenTo(email) }))
        {
            Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        }

        await LoginAsync(client, email, TestUsers.Password);
        await using var db = CreateContext();
        var id = await db.ApplicationUsers.Where(x => x.Email == email).Select(x => x.Id).SingleAsync();
        return new AcceptanceUser(id, email, client);
    }

    /// <summary>Login with CSRF, then refreshes the CSRF header (antiforgery tokens are bound to the identity).</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, bool ensureSuccess = true)
    {
        await RelativesTestSupport.AddCsrfAsync(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        if (ensureSuccess)
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            await RelativesTestSupport.AddCsrfAsync(client);
        }

        return login;
    }

    public string LastTokenTo(string email) =>
        RelativesTestSupport.ExtractToken(Emails.Sent.Last(x => string.Equals(x.To, email, StringComparison.OrdinalIgnoreCase)).Body);

    /// <summary>Owner invites, relative accepts with the token from the invitation email. Returns the relationship id.</summary>
    public async Task<Guid> InviteAndAcceptAsync(AcceptanceUser owner, AcceptanceUser relative)
    {
        using (var invite = await RelativesTestSupport.InviteAsync(owner.Client, relative.Email))
        {
            Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        }

        using var accept = await RelativesTestSupport.AcceptAsync(relative.Client, LastTokenTo(relative.Email));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        return (await RelativesTestSupport.ReadJsonAsync(accept)).GetProperty("relationshipId").GetGuid();
    }

    // ---- Devices ----

    /// <summary>Factory provisioning (operations CLI path, P2-02) — the simulator gets the one-time API key + pairing code.</summary>
    public async Task<DeviceSimulator> ProvisionDeviceAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var result = await new DeviceOnboardingService(
                services.GetRequiredService<AppDbContext>(),
                services.GetRequiredService<ITokenHasher>(),
                services.GetRequiredService<IClock>())
            .ProvisionAsync($"serial-{Guid.NewGuid():N}");
        return new DeviceSimulator(CreateClient(), result, Clock);
    }

    public async Task<DeviceSimulator> ProvisionAndPairAsync(AcceptanceUser owner)
    {
        var device = await ProvisionDeviceAsync();
        using var pair = await PairAsync(owner, device.PairingCode);
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);
        return device;
    }

    public static Task<HttpResponseMessage> PairAsync(AcceptanceUser user, string code) =>
        user.Client.PostAsJsonAsync("/api/devices/pair", new { code });

    // ---- Schedules ----

    public async Task<Guid> CreateScheduleAsync(AcceptanceUser owner, int slot, string time, int grace, params string[] medicines)
    {
        using var response = await owner.Client.PostAsJsonAsync("/api/schedules", ScheduleBody(slot, time, grace, medicines));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await RelativesTestSupport.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    public object ScheduleBody(int slot, string time, int grace, params string[] medicines) => new
    {
        compartmentSlot = slot,
        timeOfDay = time,
        daysOfWeek = new[] { 1, 2, 3, 4, 5, 6, 7 },
        startDate = Day,
        gracePeriodMinutes = grace,
        timeZoneId = TimeZoneId,
        items = medicines.Select(name => new { medicineName = name, quantity = 1m, unit = "viên", instructions = "Sau ăn" }),
    };

    // ---- Background jobs, run deterministically with the job's own arguments ----

    public async Task<DoseGenerationResult> GenerateDosesAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var now = Clock.UtcNow;
        return await scope.ServiceProvider.GetRequiredService<DoseGenerationService>().GenerateAsync(now.AddHours(-24), now.AddHours(48));
    }

    public async Task<AlertEvaluationResult> EvaluateAlertsAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AlertEvaluationService>().EvaluateAsync();
    }

    /// <summary>Runs the email worker until nothing is due at the current fake time; returns each result.</summary>
    public async Task<List<EmailDeliveryResult>> DeliverEmailsAsync()
    {
        var results = new List<EmailDeliveryResult>();
        while (true)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<EmailDeliveryWorker>().ProcessNextAsync();
            if (result == EmailDeliveryResult.NoDelivery) return results;
            results.Add(result);
        }
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await RelativesTestSupport.ReadJsonAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.ForEach(x => x.Dispose());
        await Factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}
