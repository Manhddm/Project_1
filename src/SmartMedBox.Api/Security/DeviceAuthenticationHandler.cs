using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Api.Errors;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Api.Security;

/// <summary>
/// Xác thực ESP32 bằng X-Device-Id + X-Device-Api-Key (D-07). Scheme riêng: chỉ endpoint thiết bị dùng,
/// không đọc cookie người dùng. Mọi trường hợp sai trả cùng 401 DEVICE_UNAUTHORIZED; không log header.
/// </summary>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    ITokenHasher hasher)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Device";
    public const string DeviceIdHeader = "X-Device-Id";
    public const string ApiKeyHeader = "X-Device-Api-Key";
    public const string DeviceKeyClaim = "device_key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? deviceId = Request.Headers[DeviceIdHeader];
        string? apiKey = Request.Headers[ApiKeyHeader];
        if (!DeviceApiKey.IsWellFormedDeviceId(deviceId) || !DeviceApiKey.IsWellFormedKey(apiKey))
        {
            return AuthenticateResult.Fail("Invalid device credentials.");
        }

        var device = await db.Devices.AsNoTracking()
            .Where(x => x.DeviceId == deviceId)
            .Select(x => new { x.Id, x.DeviceApiKeyHash })
            .SingleOrDefaultAsync(Context.RequestAborted);

        // Thiết bị không tồn tại vẫn tính HMAC để thời gian phản hồi không lộ sự tồn tại.
        if (!DeviceApiKey.Matches(hasher, apiKey!, device?.DeviceApiKeyHash ?? string.Empty) || device is null)
        {
            return AuthenticateResult.Fail("Invalid device credentials.");
        }

        // Không dùng NameIdentifier: principal thiết bị không bao giờ được hiểu là người dùng.
        var identity = new ClaimsIdentity([new Claim(DeviceKeyClaim, device.Id.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        ApiProblems.WriteAsync(Context, StatusCodes.Status401Unauthorized, ErrorCodes.DeviceUnauthorized,
            cancellationToken: Context.RequestAborted);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => HandleChallengeAsync(properties);
}
