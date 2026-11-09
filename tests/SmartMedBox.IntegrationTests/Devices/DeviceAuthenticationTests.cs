using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceAuthenticationTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    private const string ProbeUrl = "/api/test-probe/device-auth";

    [Fact]
    public async Task DeviceEndpoint_ValidHeaders_AuthenticatesDevice()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var device = await ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var response = await client.SendAsync(DeviceRequest(device.Provisioned.Device.DeviceId, device.DeviceApiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(device.Provisioned.Device.Id.ToString(), body.RootElement.GetProperty("deviceKey").GetString());
    }

    [Fact]
    public async Task DeviceEndpoint_MissingWrongOrUnknownCredentials_ReturnsUniform401()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var device = await ProvisionAsync(factory);
        var deviceId = device.Provisioned.Device.DeviceId;
        using var client = TestUsers.CreateClient(factory);

        HttpRequestMessage[] requests =
        [
            DeviceRequest(null, null),
            DeviceRequest(deviceId, null),
            DeviceRequest(null, device.DeviceApiKey),
            DeviceRequest(deviceId, DeviceApiKey.Create()),
            DeviceRequest("SMB-UNKNOWN0", device.DeviceApiKey),
            DeviceRequest(deviceId, device.DeviceApiKey[..42]),
            DeviceRequest(device.Provisioned.Device.SerialNumber, device.DeviceApiKey),
        ];

        var bodies = new List<string>();
        foreach (var request in requests)
        {
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("DEVICE_UNAUTHORIZED", body.RootElement.GetProperty("code").GetString());
            Assert.True(body.RootElement.TryGetProperty("traceId", out _));
            bodies.Add(body.RootElement.GetProperty("detail").GetString() + "|" + body.RootElement.GetProperty("title").GetString());
        }

        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task DeviceEndpoint_UserCookieOnly_Returns401DeviceUnauthorized()
    {
        var user = await TestUsers.SeedAsync(fixture);
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        using var client = await TestUsers.LoginAsync(factory, user);

        using var response = await client.SendAsync(DeviceRequest(null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_UNAUTHORIZED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UserEndpoint_ValidDeviceHeaders_Returns401Unauthorized()
    {
        using var factory = new TestApplicationFactory(fixture.ConnectionString);
        var device = await ProvisionAsync(factory);
        using var client = TestUsers.CreateClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Add("X-Device-Id", device.Provisioned.Device.DeviceId);
        request.Headers.Add("X-Device-Api-Key", device.DeviceApiKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", body.RootElement.GetProperty("code").GetString());
    }

    internal static async Task<ProvisioningResult> ProvisionAsync(TestApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        return await new DeviceOnboardingService(
                services.GetRequiredService<AppDbContext>(),
                services.GetRequiredService<ITokenHasher>(),
                services.GetRequiredService<IClock>())
            .ProvisionAsync($"serial-{Guid.NewGuid():N}");
    }

    internal static HttpRequestMessage DeviceRequest(string? deviceId, string? apiKey, string url = ProbeUrl)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (deviceId is not null)
        {
            request.Headers.Add("X-Device-Id", deviceId);
        }

        if (apiKey is not null)
        {
            request.Headers.Add("X-Device-Api-Key", apiKey);
        }

        return request;
    }
}
