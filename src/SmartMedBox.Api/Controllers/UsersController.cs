using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var user = await db.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == userId && !x.IsDeleted)
            .Select(x => new { x.Id, x.Email, x.EmailVerified, x.CreatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var device = await db.Devices
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => new { x.Id, x.SerialNumber })
            .SingleOrDefaultAsync(cancellationToken);

        OwnedDeviceDto? ownedDevice = null;
        if (device is not null)
        {
            var pairedAt = await db.DevicePairingCodes
                .AsNoTracking()
                .Where(x => x.DeviceId == device.Id && x.UsedAt != null)
                .OrderByDescending(x => x.UsedAt)
                .Select(x => x.UsedAt)
                .FirstOrDefaultAsync(cancellationToken);

            ownedDevice = new OwnedDeviceDto(device.Id, device.SerialNumber, pairedAt);
        }

        return Ok(new MeResponse(user.Id, user.Email, user.EmailVerified, user.CreatedAt, ownedDevice));
    }
}
