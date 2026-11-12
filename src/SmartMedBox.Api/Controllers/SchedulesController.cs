using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Infrastructure.Schedules;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/schedules")]
public sealed class SchedulesController(ScheduleService schedules) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? deviceId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (deviceId is null)
        {
            return ApiProblemResults.Validation(HttpContext, "deviceId", "deviceId là bắt buộc.");
        }

        var (outcome, items) = await schedules.ListAsync(userId, deviceId.Value, cancellationToken);
        return outcome == ScheduleOutcome.Ok
            ? Ok(new ScheduleListResponse(items.Select(ToDto).ToList()))
            : Problem(outcome);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var (outcome, schedule) = await schedules.GetAsync(userId, id, cancellationToken);
        return outcome == ScheduleOutcome.Ok ? Ok(ToDto(schedule!)) : Problem(outcome);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScheduleRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await schedules.CreateAsync(userId, ToInput(request), cancellationToken);
        if (result.Outcome != ScheduleOutcome.Ok)
        {
            return Problem(result.Outcome);
        }

        return CreatedAtAction(
            nameof(Get),
            new { id = result.ScheduleId },
            new ScheduleCreatedResponse(result.ScheduleId, result.ScheduleVersion, result.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ScheduleRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await schedules.UpdateAsync(userId, id, ToInput(request), cancellationToken);
        return result.Outcome == ScheduleOutcome.Ok
            ? Ok(new ScheduleUpdatedResponse(result.ScheduleId, result.ScheduleVersion))
            : Problem(result.Outcome);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var outcome = await schedules.DeleteAsync(userId, id, cancellationToken);
        return outcome == ScheduleOutcome.Ok ? NoContent() : Problem(outcome);
    }

    private new IActionResult Unauthorized() =>
        ApiProblemResults.Create(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);

    private IActionResult Problem(ScheduleOutcome outcome) => outcome switch
    {
        ScheduleOutcome.Forbidden => ApiProblemResults.Create(HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
        ScheduleOutcome.NotFound => ApiProblemResults.Create(HttpContext, StatusCodes.Status404NotFound, ErrorCodes.ScheduleNotFound),
        ScheduleOutcome.DeviceNotPaired => ApiProblemResults.Create(HttpContext, StatusCodes.Status409Conflict, ErrorCodes.DeviceNotPaired),
        ScheduleOutcome.CompartmentInvalid => ApiProblemResults.Create(HttpContext, StatusCodes.Status400BadRequest, ErrorCodes.CompartmentInvalid),
        ScheduleOutcome.InvalidTimeZone => ApiProblemResults.Create(HttpContext, StatusCodes.Status400BadRequest, ErrorCodes.ScheduleInvalidTimezone),
        ScheduleOutcome.InvalidDays => ApiProblemResults.Validation(HttpContext, "daysOfWeek", "Ngày trong tuần phải từ 1 đến 7, không trùng, ít nhất một ngày."),
        ScheduleOutcome.InvalidDateRange => ApiProblemResults.Validation(HttpContext, "endDate", "Ngày kết thúc phải từ ngày bắt đầu trở đi."),
        ScheduleOutcome.Overlap => ApiProblemResults.Create(
            HttpContext,
            StatusCodes.Status409Conflict,
            ErrorCodes.ScheduleOverlap,
            "Lịch chồng lấn với một lịch khác cùng ngăn (cùng thứ, khung ngày và cửa sổ giờ giao nhau)."),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    // Model validation ([ApiController]) đã đảm bảo các trường bắt buộc khác null trước khi tới đây.
    private static ScheduleInput ToInput(ScheduleRequest request) => new(
        request.CompartmentSlot!.Value,
        TimeOnly.ParseExact(request.TimeOfDay, "HH:mm", CultureInfo.InvariantCulture),
        request.DaysOfWeek!,
        request.StartDate!.Value,
        request.EndDate,
        request.GracePeriodMinutes!.Value,
        request.TimeZoneId.Trim(),
        request.Items!.Select(x => new ScheduleItemInput(x.MedicineName, x.Quantity!.Value, x.Unit, x.Instructions)).ToList());

    private static ScheduleDto ToDto(ScheduleView view) => new(
        view.Id,
        view.DeviceId,
        view.CompartmentSlot,
        view.CompartmentName,
        view.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture),
        view.DaysOfWeek,
        view.StartDate,
        view.EndDate,
        view.GracePeriodMinutes,
        view.TimeZoneId,
        view.Items.Select(x => new ScheduleItemDto(x.Id, x.MedicineName, x.Quantity, x.Unit, x.Instructions)).ToList(),
        view.Version,
        view.UpdatedAt);
}
