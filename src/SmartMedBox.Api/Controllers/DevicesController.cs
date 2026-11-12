using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Api.Controllers;

/// <summary>API thiết bị cho người dùng Web (API_CONTRACT §4).</summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public sealed class DevicesController(
    DeviceOwnershipService ownership, IClock clock, IOptions<DeviceConnectivityOptions> connectivity) : ControllerBase
{
    [HttpPost("pair")]
    [EnableRateLimiting(RateLimitPolicies.Pair)]
    public async Task<IActionResult> Pair([FromBody] PairDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var result = await ownership.PairAsync(userId, request.Code, cancellationToken);
        return result.Outcome switch
        {
            PairingOutcome.Success => Ok(new PairDeviceResponse(new PairedDeviceDto(
                result.Device!.Id,
                result.Device.SerialNumber,
                result.PairedAt!.Value,
                result.Device.ScheduleVersion,
                ToDto(result.Device.Compartments)))),
            PairingOutcome.EmailNotVerified => Fail(StatusCodes.Status403Forbidden, ErrorCodes.EmailNotVerified),
            PairingOutcome.CodeInvalid => Fail(StatusCodes.Status400BadRequest, ErrorCodes.PairingCodeInvalid),
            PairingOutcome.CodeUsed => Fail(StatusCodes.Status409Conflict, ErrorCodes.PairingCodeUsed),
            PairingOutcome.CodeExpired => Fail(StatusCodes.Status410Gone, ErrorCodes.PairingCodeExpired),
            PairingOutcome.UserAlreadyOwnsDevice => Fail(StatusCodes.Status409Conflict, ErrorCodes.UserAlreadyOwnsDevice),
            PairingOutcome.DeviceAlreadyPaired => Fail(StatusCodes.Status409Conflict, ErrorCodes.DeviceAlreadyPaired),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, null),
        };
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var device = await ownership.GetOwnedAsync(userId, cancellationToken);
        return device is null
            ? Fail(StatusCodes.Status404NotFound, ErrorCodes.DeviceNotFound)
            : Ok(new MyDeviceDto(
                device.Id,
                device.SerialNumber,
                device.ScheduleVersion,
                device.LastSeenAt,
                IsOnline: connectivity.Value.IsOnline(device.LastSeenAt, clock.UtcNow),
                ToDto(device.Compartments)));
    }

    private static List<CompartmentDto> ToDto(IReadOnlyList<OwnedCompartment> compartments) =>
        compartments.Select(x => new CompartmentDto(x.SlotNumber, x.Name, x.IsEnabled)).ToList();

    private IActionResult Fail(int status, string code) => ApiProblemResults.Create(HttpContext, status, code);
}
