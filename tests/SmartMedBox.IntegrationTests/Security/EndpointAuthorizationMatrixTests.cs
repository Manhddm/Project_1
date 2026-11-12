using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SmartMedBox.Api.Security;
using SmartMedBox.IntegrationTests.Devices;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Security;

/// <summary>
/// P7-03: kiểm kê mọi endpoint thật của app. Endpoint mới chưa được phân loại ở <see cref="Matrix"/> làm test fail,
/// buộc người thêm endpoint quyết định nhóm quyền + CSRF (API_CONTRACT §11).
/// </summary>
public sealed partial class EndpointAuthorizationMatrixTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>
{
    public enum Access
    {
        /// <summary>Không cần đăng nhập (auth/register/login/forgot/reset/verify + trang tài khoản công khai).</summary>
        Anonymous,

        /// <summary>API cần cookie người dùng; anonymous → 401 UNAUTHORIZED.</summary>
        User,

        /// <summary>Như <see cref="User"/>, thêm kiểm tra chủ hộp trong service (relative → 403/404, có test riêng).</summary>
        OwnerOnly,

        /// <summary>Trang Razor cần đăng nhập; anonymous → chuyển hướng /account/login.</summary>
        UserPage,

        /// <summary>Endpoint ESP32: chỉ X-Device-Id + X-Device-Api-Key; cookie → 401 DEVICE_UNAUTHORIZED.</summary>
        Device,

        /// <summary>Health check công khai, chỉ trả Healthy/Unhealthy.</summary>
        Health,
    }

    /// <summary>(Phân loại quyền, có miễn CSRF hay không). Khóa: "METHOD route-pattern".</summary>
    private static readonly Dictionary<string, (Access Access, bool CsrfExempt)> Matrix = new()
    {
        // Trang công khai / Razor
        ["GET "] = (Access.Anonymous, false),
        ["GET {controller:notapi}/{action=Index}/{id?}"] = (Access.Anonymous, false),
        ["GET account"] = (Access.UserPage, false),
        ["GET account/register"] = (Access.Anonymous, false),
        ["POST account/register"] = (Access.Anonymous, false),
        ["GET account/verify-email"] = (Access.Anonymous, false),
        ["POST account/verify-email"] = (Access.Anonymous, false),
        ["GET account/resend-verification"] = (Access.Anonymous, false),
        ["POST account/resend-verification"] = (Access.Anonymous, false),
        ["GET account/login"] = (Access.Anonymous, false),
        ["POST account/login"] = (Access.Anonymous, false),
        ["POST account/logout"] = (Access.Anonymous, false),
        ["GET account/forgot-password"] = (Access.Anonymous, false),
        ["POST account/forgot-password"] = (Access.Anonymous, false),
        ["GET account/reset-password"] = (Access.Anonymous, false),
        ["POST account/reset-password"] = (Access.Anonymous, false),
        ["GET alerts"] = (Access.UserPage, false),
        ["GET dashboard"] = (Access.UserPage, false),
        ["GET devices"] = (Access.UserPage, false),
        ["GET devices/pair"] = (Access.UserPage, false),
        ["POST devices/pair"] = (Access.UserPage, false),
        ["GET prescriptions"] = (Access.UserPage, false),
        ["GET relatives"] = (Access.UserPage, false),
        ["GET schedules"] = (Access.UserPage, false),

        // API auth (D-14). Login/logout cần CSRF; các luồng token ẩn danh miễn CSRF (không gắn phiên).
        ["POST api/auth/register"] = (Access.Anonymous, true),
        ["POST api/auth/verify-email"] = (Access.Anonymous, true),
        ["POST api/auth/resend-verification"] = (Access.Anonymous, true),
        ["POST api/auth/forgot-password"] = (Access.Anonymous, true),
        ["POST api/auth/reset-password"] = (Access.Anonymous, true),
        ["POST api/auth/login"] = (Access.Anonymous, false),
        ["POST api/auth/logout"] = (Access.Anonymous, false),

        // API người dùng
        ["GET api/users/me"] = (Access.User, false),
        ["POST api/devices/pair"] = (Access.User, false),
        ["GET api/devices/mine"] = (Access.OwnerOnly, false),
        ["GET api/dashboard"] = (Access.User, false),
        ["GET api/dose-history"] = (Access.User, false),
        ["GET api/alerts"] = (Access.User, false),
        ["POST api/alerts/{id:guid}/read"] = (Access.User, false),
        ["GET api/schedules"] = (Access.User, false),
        ["GET api/schedules/{id:guid}"] = (Access.User, false),
        ["POST api/schedules"] = (Access.OwnerOnly, false),
        ["PUT api/schedules/{id:guid}"] = (Access.OwnerOnly, false),
        ["DELETE api/schedules/{id:guid}"] = (Access.OwnerOnly, false),
        ["GET api/relatives/relationships"] = (Access.User, false),
        ["DELETE api/relatives/relationships/{id:guid}"] = (Access.User, false),
        ["POST api/relatives/invitations"] = (Access.OwnerOnly, false),
        ["GET api/relatives/invitations"] = (Access.OwnerOnly, false),
        ["POST api/relatives/invitations/{id:guid}/revoke"] = (Access.OwnerOnly, false),
        ["POST api/relatives/invitations/accept"] = (Access.User, false),
        ["GET api/prescriptions/images"] = (Access.OwnerOnly, false),
        ["POST api/prescriptions/images"] = (Access.OwnerOnly, false),
        ["GET api/prescriptions/images/{id:guid}/content"] = (Access.OwnerOnly, false),
        ["DELETE api/prescriptions/images/{id:guid}"] = (Access.OwnerOnly, false),

        // Thiết bị (D-07)
        ["POST api/device/heartbeat"] = (Access.Device, true),
        ["GET api/device/schedule-sync"] = (Access.Device, true),
        ["POST api/device/events/batch"] = (Access.Device, true),

        ["GET /health/live"] = (Access.Health, false),
        ["GET /health/ready"] = (Access.Health, false),
    };

    [Fact]
    public void EveryEndpoint_IsClassified_AndCsrfExemptionMatchesTable()
    {
        using var factory = CreateFactory();
        var endpoints = ProductEndpoints(factory);

        var unclassified = endpoints.Select(e => e.Key).Where(k => !Matrix.ContainsKey(k)).ToList();
        var stale = Matrix.Keys.Except(endpoints.Select(e => e.Key)).ToList();
        Assert.True(unclassified.Count == 0, "Endpoint chưa phân loại quyền: " + string.Join(", ", unclassified));
        Assert.True(stale.Count == 0, "Bảng phân loại có endpoint không còn tồn tại: " + string.Join(", ", stale));

        var csrfMismatch = endpoints
            .Where(e => e.CsrfExempt != Matrix[e.Key].CsrfExempt)
            .Select(e => $"{e.Key} (exempt thực tế={e.CsrfExempt})")
            .ToList();
        Assert.True(csrfMismatch.Count == 0, "Miễn CSRF không khớp bảng: " + string.Join(", ", csrfMismatch));
    }

    [Fact]
    public async Task UserEndpoints_Anonymous_Return401OrRedirectToLogin()
    {
        using var factory = CreateFactory();
        using var client = CreateNoRedirectClient(factory);

        foreach (var (key, request) in Requests(factory, Access.User, Access.OwnerOnly, Access.UserPage))
        {
            await AssertRejectedAsUserAsync(client, key, request);
        }
    }

    [Fact]
    public async Task UserEndpoints_WithValidDeviceCredentials_StillRejected()
    {
        using var factory = CreateFactory();
        var device = await DeviceAuthenticationTests.ProvisionAsync(factory);
        using var client = CreateNoRedirectClient(factory);

        foreach (var (key, request) in Requests(factory, Access.User, Access.OwnerOnly, Access.UserPage))
        {
            request.Headers.Add(DeviceAuthenticationHandler.DeviceIdHeader, device.Provisioned.Device.DeviceId);
            request.Headers.Add(DeviceAuthenticationHandler.ApiKeyHeader, device.DeviceApiKey);
            await AssertRejectedAsUserAsync(client, key, request);
        }
    }

    [Fact]
    public async Task DeviceEndpoints_WithUserCookieAndCsrf_Return401DeviceUnauthorized()
    {
        using var factory = CreateFactory();
        var user = await TestUsers.SeedAsync(fixture);
        using var client = await Relatives.RelativesTestSupport.LoginWithCsrfAsync(factory, user);

        foreach (var (key, request) in Requests(factory, Access.Device))
        {
            using var response = await client.SendAsync(request);
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "DEVICE_UNAUTHORIZED", key);
        }
    }

    [Fact]
    public async Task WriteEndpoints_LoggedInWithoutCsrfHeader_Return400CsrfInvalid()
    {
        using var factory = CreateFactory();
        var user = await TestUsers.SeedAsync(fixture);
        using var client = await TestUsers.LoginAsync(factory, user);
        var writes = Requests(factory, Access.Anonymous, Access.User, Access.OwnerOnly, Access.UserPage)
            .Where(r => r.Request.Method != HttpMethod.Get && !Matrix[r.Key].CsrfExempt)
            .ToList();
        Assert.NotEmpty(writes);

        foreach (var (key, request) in writes)
        {
            using var response = await client.SendAsync(request);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "CSRF_INVALID", key);
        }
    }

    private static async Task AssertRejectedAsUserAsync(HttpClient client, string key, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        if (Matrix[key].Access == Access.UserPage)
        {
            // Razor POST bị chặn ở CSRF trước khi tới action; GET chuyển hướng về đăng nhập.
            if (request.Method == HttpMethod.Get)
            {
                Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"{key}: {response.StatusCode}");
                Assert.StartsWith("/account/login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                await AssertProblemAsync(response, HttpStatusCode.BadRequest, "CSRF_INVALID", key);
            }

            return;
        }

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED", key);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string key)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{key}: mong đợi {(int)status}, thực tế {(int)response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        Assert.True(code == json.RootElement.GetProperty("code").GetString(), $"{key}: mong đợi {code}, thực tế {body}");
    }

    private static List<(string Key, HttpRequestMessage Request)> Requests(WebApplicationFactory<Program> factory, params Access[] classes) =>
        ProductEndpoints(factory)
            .Where(e => classes.Contains(Matrix[e.Key].Access))
            .Select(e =>
            {
                var request = new HttpRequestMessage(new HttpMethod(e.Method), "/" + RouteParameter().Replace(e.Pattern.TrimStart('/'), Guid.NewGuid().ToString()));
                if (e.Method != "GET")
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                return (e.Key, request);
            })
            .ToList();

    private static List<(string Key, string Method, string Pattern, bool CsrfExempt)> ProductEndpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly != typeof(EndpointAuthorizationMatrixTests).Assembly)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (
                    Key: $"{method} {e.RoutePattern.RawText}",
                    Method: method,
                    Pattern: e.RoutePattern.RawText ?? string.Empty,
                    CsrfExempt: e.Metadata.GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null)))
            .Where(e => !e.Pattern.Contains("{controller", StringComparison.Ordinal) || e.Method == "GET")
            .DistinctBy(e => e.Key)
            .ToList();

    private static HttpClient CreateNoRedirectClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private WebApplicationFactory<Program> CreateFactory() =>
        new TestApplicationFactory(fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            // Test duyệt nhiều endpoint pair/login; ngưỡng rate limit có test riêng (RateLimitingTests).
            builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
            builder.UseSetting("RateLimiting:Pair:PermitLimit", "1000");
            builder.UseSetting("RateLimiting:InvitationCreate:PermitLimit", "1000");
            builder.UseSetting("RateLimiting:InvitationAccept:PermitLimit", "1000");
        });

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
