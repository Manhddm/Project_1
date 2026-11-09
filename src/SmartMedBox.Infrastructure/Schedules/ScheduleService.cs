using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Domain.Schedules;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Infrastructure.Schedules;

public enum ScheduleOutcome
{
    Ok,
    Forbidden,
    NotFound,
    DeviceNotPaired,
    CompartmentInvalid,
    InvalidTimeZone,
    InvalidDays,
    InvalidDateRange,
}

public sealed record ScheduleItemInput(string MedicineName, decimal Quantity, string Unit, string? Instructions);

public sealed record ScheduleInput(
    int CompartmentSlot,
    TimeOnly TimeOfDay,
    IReadOnlyCollection<int> DaysOfWeek,
    DateOnly StartDate,
    DateOnly? EndDate,
    int GracePeriodMinutes,
    string TimeZoneId,
    IReadOnlyCollection<ScheduleItemInput> Items);

public sealed record ScheduleItemView(Guid Id, string MedicineName, decimal Quantity, string Unit, string Instructions);

public sealed record ScheduleView(
    Guid Id,
    Guid DeviceId,
    int CompartmentSlot,
    string CompartmentName,
    TimeOnly TimeOfDay,
    IReadOnlyList<int> DaysOfWeek,
    DateOnly StartDate,
    DateOnly? EndDate,
    int GracePeriodMinutes,
    string TimeZoneId,
    IReadOnlyList<ScheduleItemView> Items,
    int Version);

public sealed record ScheduleWriteResult(ScheduleOutcome Outcome, Guid ScheduleId = default, int ScheduleVersion = 0);

/// <summary>
/// CRUD lịch uống (P3-02). Quyền theo hộp qua <see cref="DeviceAccessService"/>; <c>userId</c> luôn lấy từ phiên.
/// Mọi thay đổi tăng <c>Devices.ScheduleVersion</c> + ghi audit trong cùng transaction.
/// D-22: không bao giờ chạm <c>DoseInstances</c>/<c>DoseItemSnapshots</c> đã sinh.
/// </summary>
public sealed class ScheduleService(AppDbContext db, DeviceAccessService access, IClock clock)
{
    public async Task<(ScheduleOutcome Outcome, IReadOnlyList<ScheduleView> Items)> ListAsync(
        Guid userId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanAsync(userId, deviceId, DeviceOperation.Read, cancellationToken))
        {
            return (ScheduleOutcome.Forbidden, []);
        }

        var ids = await db.MedicationSchedules
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return (ScheduleOutcome.Ok, await LoadViewsAsync(ids, cancellationToken));
    }

    public async Task<(ScheduleOutcome Outcome, ScheduleView? Schedule)> GetAsync(
        Guid userId,
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        var deviceId = await FindDeviceIdAsync(scheduleId, cancellationToken);
        if (deviceId is null)
        {
            return (ScheduleOutcome.NotFound, null);
        }

        if (!await access.CanAsync(userId, deviceId.Value, DeviceOperation.Read, cancellationToken))
        {
            return (ScheduleOutcome.Forbidden, null);
        }

        return (ScheduleOutcome.Ok, (await LoadViewsAsync([scheduleId], cancellationToken)).Single());
    }

    public async Task<ScheduleWriteResult> CreateAsync(
        Guid userId,
        ScheduleInput input,
        CancellationToken cancellationToken = default)
    {
        // Hộp lấy từ tài khoản chủ, không nhận deviceId từ client (API_CONTRACT §5.3).
        var deviceId = await db.Devices
            .Where(x => x.OwnerUserId == userId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (deviceId is null)
        {
            var isRelative = await db.CareRelationships.AnyAsync(
                x => x.RelativeUserId == userId && x.Status == RelationshipStatus.Active,
                cancellationToken);
            return new ScheduleWriteResult(isRelative ? ScheduleOutcome.Forbidden : ScheduleOutcome.DeviceNotPaired);
        }

        var schedule = new MedicationSchedule { Id = Guid.NewGuid(), DeviceId = deviceId.Value };
        var outcome = await ApplyAsync(schedule, input, cancellationToken);
        if (outcome != ScheduleOutcome.Ok)
        {
            return new ScheduleWriteResult(outcome);
        }

        db.MedicationSchedules.Add(schedule);
        return await CommitAsync(userId, schedule, input, AuditActions.ScheduleCreated, replaceChildren: false, cancellationToken);
    }

    public async Task<ScheduleWriteResult> UpdateAsync(
        Guid userId,
        Guid scheduleId,
        ScheduleInput input,
        CancellationToken cancellationToken = default)
    {
        var schedule = await db.MedicationSchedules.SingleOrDefaultAsync(
            x => x.Id == scheduleId && !x.IsDeleted,
            cancellationToken);
        if (schedule is null)
        {
            return new ScheduleWriteResult(ScheduleOutcome.NotFound);
        }

        if (!await access.CanAsync(userId, schedule.DeviceId, DeviceOperation.Manage, cancellationToken))
        {
            return new ScheduleWriteResult(ScheduleOutcome.Forbidden);
        }

        var outcome = await ApplyAsync(schedule, input, cancellationToken);
        if (outcome != ScheduleOutcome.Ok)
        {
            return new ScheduleWriteResult(outcome);
        }

        return await CommitAsync(userId, schedule, input, AuditActions.ScheduleUpdated, replaceChildren: true, cancellationToken);
    }

    public async Task<ScheduleOutcome> DeleteAsync(Guid userId, Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await db.MedicationSchedules.SingleOrDefaultAsync(
            x => x.Id == scheduleId && !x.IsDeleted,
            cancellationToken);
        if (schedule is null)
        {
            return ScheduleOutcome.NotFound;
        }

        if (!await access.CanAsync(userId, schedule.DeviceId, DeviceOperation.Manage, cancellationToken))
        {
            return ScheduleOutcome.Forbidden;
        }

        // Xóa mềm: giữ hàng lịch/ngày/thuốc để lịch sử dose còn truy vết được.
        schedule.IsDeleted = true;
        await CommitAsync(userId, schedule, input: null, AuditActions.ScheduleDeleted, replaceChildren: false, cancellationToken);
        return ScheduleOutcome.Ok;
    }

    public static bool IsIanaTimeZone(string timeZoneId) =>
        // TryConvertIanaIdToWindowsId loại id kiểu Windows ("SE Asia Standard Time") mà FindSystemTimeZoneById vẫn nhận trên Windows.
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _);

    private async Task<ScheduleOutcome> ApplyAsync(MedicationSchedule schedule, ScheduleInput input, CancellationToken cancellationToken)
    {
        if (!IsIanaTimeZone(input.TimeZoneId))
        {
            return ScheduleOutcome.InvalidTimeZone;
        }

        var compartmentId = await db.Compartments
            .Where(x => x.DeviceId == schedule.DeviceId && x.SlotNumber == input.CompartmentSlot)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (compartmentId is null)
        {
            return ScheduleOutcome.CompartmentInvalid;
        }

        schedule.CompartmentId = compartmentId.Value;
        schedule.TimeOfDay = input.TimeOfDay;
        schedule.StartDate = input.StartDate;
        schedule.EndDate = input.EndDate;
        schedule.GracePeriodMinutes = input.GracePeriodMinutes;
        schedule.TimeZoneId = input.TimeZoneId;

        try
        {
            MedicationScheduleRules.EnsureValid(schedule, input.DaysOfWeek);
        }
        catch (ArgumentException ex)
        {
            // Grace và timezone rỗng đã bị chặn ở DTO; còn lại chỉ là ngày trong tuần hoặc khoảng ngày.
            return ex.ParamName == "daysOfWeek" ? ScheduleOutcome.InvalidDays : ScheduleOutcome.InvalidDateRange;
        }

        return ScheduleOutcome.Ok;
    }

    private async Task<ScheduleWriteResult> CommitAsync(
        Guid userId,
        MedicationSchedule schedule,
        ScheduleInput? input,
        string action,
        bool replaceChildren,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Tăng nguyên tử ở DB (khóa hàng Devices) để hai lần ghi đồng thời không mất một lần tăng.
        await db.Devices
            .Where(x => x.Id == schedule.DeviceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScheduleVersion, x => x.ScheduleVersion + 1), cancellationToken);

        if (replaceChildren)
        {
            await db.ScheduleDays.Where(x => x.ScheduleId == schedule.Id).ExecuteDeleteAsync(cancellationToken);
            await db.ScheduleItems.Where(x => x.ScheduleId == schedule.Id).ExecuteDeleteAsync(cancellationToken);
        }

        if (input is not null)
        {
            db.ScheduleDays.AddRange(input.DaysOfWeek.Select(day => new ScheduleDay { ScheduleId = schedule.Id, DayOfWeek = day }));
            db.ScheduleItems.AddRange(input.Items.Select(item => new ScheduleItem
            {
                Id = Guid.NewGuid(),
                ScheduleId = schedule.Id,
                MedicineName = item.MedicineName.Trim(),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                Instructions = item.Instructions?.Trim() ?? string.Empty,
            }));
        }

        var version = await db.Devices
            .Where(x => x.Id == schedule.DeviceId)
            .Select(x => x.ScheduleVersion)
            .SingleAsync(cancellationToken);

        // Audit chỉ ghi id + version, không ghi tên thuốc/liều (dữ liệu sức khỏe).
        AuditLogWriter.Add(
            db,
            clock.UtcNow,
            action,
            nameof(MedicationSchedule),
            schedule.Id.ToString(),
            userId,
            schedule.DeviceId,
            $"{{\"scheduleVersion\":{version}}}");

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ScheduleWriteResult(ScheduleOutcome.Ok, schedule.Id, version);
    }

    private Task<Guid?> FindDeviceIdAsync(Guid scheduleId, CancellationToken cancellationToken) =>
        db.MedicationSchedules
            .AsNoTracking()
            .Where(x => x.Id == scheduleId && !x.IsDeleted)
            .Select(x => (Guid?)x.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<ScheduleView>> LoadViewsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await (
                from s in db.MedicationSchedules.AsNoTracking()
                join c in db.Compartments on s.CompartmentId equals c.Id
                join d in db.Devices on s.DeviceId equals d.Id
                where ids.Contains(s.Id)
                orderby c.SlotNumber, s.TimeOfDay
                select new { Schedule = s, c.SlotNumber, CompartmentName = c.Name, d.ScheduleVersion })
            .ToListAsync(cancellationToken);
        var days = await db.ScheduleDays.AsNoTracking()
            .Where(x => ids.Contains(x.ScheduleId))
            .ToListAsync(cancellationToken);
        var items = await db.ScheduleItems.AsNoTracking()
            .Where(x => ids.Contains(x.ScheduleId))
            .OrderBy(x => x.MedicineName)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ScheduleView(
                row.Schedule.Id,
                row.Schedule.DeviceId,
                row.SlotNumber,
                row.CompartmentName,
                row.Schedule.TimeOfDay,
                days.Where(x => x.ScheduleId == row.Schedule.Id).Select(x => x.DayOfWeek).Order().ToList(),
                row.Schedule.StartDate,
                row.Schedule.EndDate,
                row.Schedule.GracePeriodMinutes,
                row.Schedule.TimeZoneId,
                items.Where(x => x.ScheduleId == row.Schedule.Id)
                    .Select(x => new ScheduleItemView(x.Id, x.MedicineName, x.Quantity, x.Unit, x.Instructions))
                    .ToList(),
                row.ScheduleVersion))
            .ToList();
    }
}
