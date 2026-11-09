using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Api.Controllers;

/// <summary>API cho ESP32 (API_CONTRACT §9): chỉ scheme thiết bị, không cookie nên không cần CSRF.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthenticationHandler.SchemeName)]
[IgnoreAntiforgeryToken]
[Route("api/device")]
public sealed class DeviceController(AppDbContext db, IClock clock) : ControllerBase
{
    // shortcut: giá trị ví dụ của API_CONTRACT §9.1; tần suất heartbeat thuộc D-12/D-26 chưa chốt.
    private const int NextHeartbeatSeconds = 60;

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(DeviceAuthenticationHandler.DeviceKeyClaim)?.Value, out var deviceKey))
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.DeviceUnauthorized);
        }

        var now = clock.UtcNow;
        await db.Devices
            .Where(x => x.Id == deviceKey)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), cancellationToken);

        var device = await db.Devices.AsNoTracking()
            .Where(x => x.Id == deviceKey)
            .Select(x => new { x.OwnerUserId, x.ScheduleVersion })
            .SingleAsync(cancellationToken);

        var paired = device.OwnerUserId is not null;
        return Ok(new HeartbeatResponse(
            (paired ? PairingState.Paired : PairingState.Unpaired).ToString(),
            now,
            paired ? device.ScheduleVersion : 0,
            NextHeartbeatSeconds));
    }
}
