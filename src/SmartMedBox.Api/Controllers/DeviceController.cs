using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;

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

    [HttpPost("events/batch")]
    public async Task<IActionResult> EventsBatch(
        [FromBody] DeviceEventBatchRequest request,
        [FromServices] DeviceEventIngestionService ingestion,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(DeviceAuthenticationHandler.DeviceKeyClaim)?.Value, out var deviceKey))
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.DeviceUnauthorized);
        }

        if (request.Events is not { Count: > 0 and <= DeviceEventRules.MaxBatchSize })
        {
            return ApiProblemResults.Validation(HttpContext, "events",
                $"Batch phải có từ 1 đến {DeviceEventRules.MaxBatchSize} sự kiện.");
        }

        if (request.Events.Any(x => x is null || !DeviceEventRules.IsValidEventId(x.EventId)))
        {
            return ApiProblemResults.Validation(HttpContext, "eventId",
                $"eventId là bắt buộc, tối đa {DeviceEventRules.MaxEventIdLength} ký tự.");
        }

        var results = await ingestion.IngestAsync(
            deviceKey,
            request.Events.Select(x => new DeviceEventInput(x.EventId!, x.CompartmentSlot, x.EventType, x.OccurredAt)).ToList(),
            cancellationToken);
        if (results is null)
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status409Conflict, ErrorCodes.DeviceNotPaired);
        }

        return Ok(new DeviceEventBatchResponse(
            clock.UtcNow,
            results.Select(x => new DeviceEventResultDto(x.EventId, x.Status.ToString(), RejectionCode(x.Rejection), null, null))
                .ToList()));
    }

    private static string? RejectionCode(DeviceEventRejection? rejection) => rejection switch
    {
        DeviceEventRejection.CompartmentInvalid => ErrorCodes.CompartmentInvalid,
        DeviceEventRejection.EventTypeInvalid => ErrorCodes.EventTypeInvalid,
        DeviceEventRejection.EventTimeInvalid => ErrorCodes.EventTimeInvalid,
        _ => null,
    };
}
