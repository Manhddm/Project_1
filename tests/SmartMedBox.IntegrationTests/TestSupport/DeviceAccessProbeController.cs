using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.IntegrationTests.TestSupport;

/// <summary>Endpoint giả mô phỏng cách endpoint theo hộp thật dùng <see cref="DeviceAccessService"/>.</summary>
[ApiController]
[Authorize]
[IgnoreAntiforgeryToken]
[Route("api/test-probe/devices/{deviceId:guid}")]
public sealed class DeviceAccessProbeController(DeviceAccessService access) : ControllerBase
{
    // userId trong query cố tình bị bỏ qua: quyền chỉ dựa trên phiên đăng nhập.
    [HttpGet]
    public Task<IActionResult> Read(Guid deviceId, [FromQuery] Guid? userId, CancellationToken cancellationToken) =>
        CheckAsync(deviceId, DeviceOperation.Read, cancellationToken);

    [HttpPut]
    public Task<IActionResult> Manage(Guid deviceId, [FromQuery] Guid? userId, CancellationToken cancellationToken) =>
        CheckAsync(deviceId, DeviceOperation.Manage, cancellationToken);

    private async Task<IActionResult> CheckAsync(Guid deviceId, DeviceOperation operation, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var callerId))
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        if (!await access.CanAsync(callerId, deviceId, operation, cancellationToken))
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        }

        return Ok(new { role = (await access.GetRoleAsync(callerId, deviceId, cancellationToken)).ToString() });
    }
}
