using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class DashboardController(AppDbContext db, DeviceAccessService access, IClock clock) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(Guid deviceId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        if (!await access.CanAsync(userId, deviceId, DeviceOperation.Read, cancellationToken)) return Forbidden();

        var device = await db.Devices.AsNoTracking().Where(x => x.Id == deviceId)
            .Select(x => new { x.Id, x.SerialNumber, x.LastSeenAt, x.ScheduleVersion }).SingleOrDefaultAsync(cancellationToken);
        if (device is null) return NotFoundProblem(ErrorCodes.DeviceNotFound);

        var now = clock.UtcNow;
        var doses = await LoadDosesAsync(deviceId, now.AddDays(-2), now.AddDays(2), cancellationToken);
        var today = doses.Where(x => x.LocalDate == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(x.TimeZoneId)).DateTime)).ToList();
        return Ok(new
        {
            device = new { device.Id, device.SerialNumber, isOnline = (bool?)null, device.LastSeenAt, device.ScheduleVersion },
            today = new { date = DateOnly.FromDateTime(now.DateTime), timeZoneId = "per-dose", doses = today },
            nextDose = doses.Where(x => x.ScheduledAt >= now && x.Status == DoseStatus.Pending).OrderBy(x => x.ScheduledAt).FirstOrDefault(),
            alerts = new { unreadCount = 0, latest = Array.Empty<object>() },
        });
    }

    [HttpPost("alerts/{id:guid}/read")]
    public async Task<IActionResult> MarkAlertRead(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();

        var recipientId = await (from recipient in db.AlertRecipients
                                 join alert in db.Alerts on recipient.AlertId equals alert.Id
                                 join dose in db.DoseInstances on alert.DoseInstanceId equals dose.Id
                                 where alert.Id == id && recipient.RecipientUserId == userId
                                 select (Guid?)recipient.Id).SingleOrDefaultAsync(cancellationToken);
        if (recipientId is null) return NotFoundProblem(ErrorCodes.AlertNotFound);

        var readAt = clock.UtcNow;
        await db.AlertRecipients
            .Where(x => x.Id == recipientId && x.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, readAt), cancellationToken);
        var storedReadAt = await db.AlertRecipients
            .Where(x => x.Id == recipientId)
            .Select(x => x.ReadAt)
            .SingleAsync(cancellationToken);
        return Ok(new { readAt = storedReadAt });
    }

    [HttpGet("dose-history")]
    public async Task<IActionResult> History(Guid deviceId, [FromQuery(Name = "from")] DateOnly? fromDate, [FromQuery(Name = "to")] DateOnly? toDate, DoseStatus? status, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        if (!await access.CanAsync(userId, deviceId, DeviceOperation.Read, cancellationToken)) return Forbidden();
        if (fromDate is not null && toDate is not null && fromDate > toDate) return ApiProblemResults.Validation(HttpContext, "from", "from phải nhỏ hơn hoặc bằng to.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var doses = await LoadDosesAsync(deviceId, fromDate?.ToDateTime(TimeOnly.MinValue) is { } start ? new DateTimeOffset(start, TimeSpan.Zero) : DateTimeOffset.MinValue, toDate?.ToDateTime(TimeOnly.MaxValue) is { } end ? new DateTimeOffset(end, TimeSpan.Zero) : DateTimeOffset.MaxValue, cancellationToken);
        var filtered = status is null ? doses : doses.Where(x => x.Status == status).ToList();
        var total = filtered.Count;
        return Ok(new { items = filtered.OrderByDescending(x => x.ScheduledAt).Skip((page - 1) * pageSize).Take(pageSize), page, pageSize, total });
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(Guid deviceId, bool unreadOnly = false, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        if (!await access.CanAsync(userId, deviceId, DeviceOperation.Read, cancellationToken)) return Forbidden();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = from alert in db.Alerts.AsNoTracking()
                    join dose in db.DoseInstances.AsNoTracking() on alert.DoseInstanceId equals dose.Id
                    join recipient in db.AlertRecipients.AsNoTracking() on alert.Id equals recipient.AlertId
                    where dose.DeviceId == deviceId && recipient.RecipientUserId == userId && (!unreadOnly || recipient.ReadAt == null)
                    select new { alert.Id, alert.DoseInstanceId, alert.AlertType, alert.Message, alert.CreatedAt, alert.ResolvedAt, recipient.ReadAt };
        var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var total = items.Count;
        return Ok(new { items = items.Skip((page - 1) * pageSize).Take(pageSize), page, pageSize, total, unreadCount = items.Count(x => x.ReadAt == null) });
    }

    private async Task<List<DoseDto>> LoadDosesAsync(Guid deviceId, DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        var rows = await (from dose in db.DoseInstances.AsNoTracking()
                          join schedule in db.MedicationSchedules.AsNoTracking() on dose.ScheduleId equals schedule.Id
                          join compartment in db.Compartments.AsNoTracking() on dose.CompartmentId equals compartment.Id
                          where dose.DeviceId == deviceId && dose.ScheduledAt >= fromInclusive && dose.ScheduledAt <= toExclusive
                          select new { dose, schedule.TimeZoneId, compartment.SlotNumber }).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.dose.Id).ToArray();
        var items = await db.DoseItemSnapshots.AsNoTracking().Where(x => ids.Contains(x.DoseInstanceId)).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId);
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.dose.ScheduledAt, zone).DateTime);
            return new DoseDto(row.dose.Id, row.SlotNumber, row.dose.ScheduledAt, row.dose.DeadlineAt, localDate, row.TimeZoneId, row.dose.Status, row.dose.ConfirmedAt, row.dose.GracePeriodMinutes,
                items.Where(x => x.DoseInstanceId == row.dose.Id).Select(x => new DoseItemDto(x.MedicineName, x.Quantity, x.Unit, x.Instructions)).ToList());
        }).ToList();
    }

    private IActionResult Forbidden() => ApiProblemResults.Create(HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
    private IActionResult NotFoundProblem(string code) => ApiProblemResults.Create(HttpContext, StatusCodes.Status404NotFound, code);
}

public sealed record DoseDto(Guid Id, int CompartmentSlot, DateTimeOffset ScheduledAt, DateTimeOffset DeadlineAt, DateOnly LocalDate, string TimeZoneId, DoseStatus Status, DateTimeOffset? ConfirmedAt, int GracePeriodMinutes, IReadOnlyList<DoseItemDto> Items);
public sealed record DoseItemDto(string MedicineName, decimal Quantity, string Unit, string Instructions);
