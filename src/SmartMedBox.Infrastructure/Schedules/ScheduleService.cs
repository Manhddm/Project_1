using System.Data;
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
    Overlap,
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

public enum ScheduleSyncOutcome
{
    Changed,
    NotModified,
    DeviceNotPaired,
}

public sealed record ScheduleSyncPackage(int Version, IReadOnlyList<ScheduleView> Schedules);

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

    /// <summary>
    /// P3-04: gói lịch hiện hành cho thiết bị (API_CONTRACT §9.2); <paramref name="deviceId"/> lấy từ xác thực thiết bị.
    /// </summary>
    public async Task<(ScheduleSyncOutcome Outcome, ScheduleSyncPackage? Package)> GetSyncPackageAsync(
        Guid deviceId,
        int clientVersion,
        CancellationToken cancellationToken = default)
    {
        // RepeatableRead giữ khóa S trên hàng Devices tới khi commit; mọi lần ghi lịch tăng version (khóa X hàng đó)
        // ở lệnh đầu tiên của transaction, nên version và lịch luôn đọc từ cùng một trạng thái đã commit.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var device = await db.Devices.AsNoTracking()
            .Where(x => x.Id == deviceId)
            .Select(x => new { x.OwnerUserId, x.ScheduleVersion })
            .SingleAsync(cancellationToken);
        if (device.OwnerUserId is null)
        {
            return (ScheduleSyncOutcome.DeviceNotPaired, null);
        }

        if (device.ScheduleVersion == clientVersion)
        {
            return (ScheduleSyncOutcome.NotModified, null);
        }

        var ids = await db.MedicationSchedules
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var views = await LoadViewsAsync(ids, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (ScheduleSyncOutcome.Changed, new ScheduleSyncPackage(device.ScheduleVersion, views));
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

        // D-23: kiểm tra sau khi đã giữ khóa hàng Devices nên hai lần ghi đồng thời vào cùng hộp được tuần tự hóa;
        // từ chối thì transaction bị rollback khi dispose (không tăng version, không audit).
        if (input is not null && await OverlapsAsync(schedule, input.DaysOfWeek, cancellationToken))
        {
            return new ScheduleWriteResult(ScheduleOutcome.Overlap);
        }

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

    private async Task<bool> OverlapsAsync(MedicationSchedule schedule, IReadOnlyCollection<int> days, CancellationToken cancellationToken)
    {
        var others = await db.MedicationSchedules
            .AsNoTracking()
            .Where(x => x.CompartmentId == schedule.CompartmentId && !x.IsDeleted && x.Id != schedule.Id)
            .Select(x => new
            {
                x.TimeOfDay,
                x.GracePeriodMinutes,
                x.StartDate,
                x.EndDate,
                x.TimeZoneId,
                Days = db.ScheduleDays.Where(d => d.ScheduleId == x.Id).Select(d => d.DayOfWeek).ToList(),
            })
            .ToListAsync(cancellationToken);

        var candidate = new OverlapWindow(schedule.TimeOfDay, schedule.GracePeriodMinutes, schedule.StartDate, schedule.EndDate, schedule.TimeZoneId, days);
        return others.Any(o => Overlaps(candidate, new OverlapWindow(o.TimeOfDay, o.GracePeriodMinutes, o.StartDate, o.EndDate, o.TimeZoneId, o.Days)));
    }

    public sealed record OverlapWindow(
        TimeOnly TimeOfDay,
        int GracePeriodMinutes,
        DateOnly StartDate,
        DateOnly? EndDate,
        string TimeZoneId,
        IReadOnlyCollection<int> DaysOfWeek);

    /// <summary>
    /// D-23: true khi có một ngày <c>d</c> của A và ngày <c>d + k</c> của B (đều trong khung ngày + thứ của từng lịch)
    /// mà cửa sổ <c>[giờ, giờ + grace]</c> giao nhau (inclusive). <c>k</c> ≠ 0 bắt cửa sổ vắt qua nửa đêm.
    /// </summary>
    public static bool Overlaps(OverlapWindow a, OverlapWindow b)
    {
        var aStart = a.StartDate.DayNumber;
        var aEnd = (a.EndDate ?? DateOnly.MaxValue).DayNumber;
        var bStart = b.StartDate.DayNumber;
        var bEnd = (b.EndDate ?? DateOnly.MaxValue).DayNumber;

        // Cùng timezone: so giờ địa phương (shift = 0). Khác timezone: quy B về khung giờ của A theo offset UTC tại mốc đầu tiên.
        // shortcut: offset lấy một lần ở ngày đầu khung giao nhau, bỏ qua DST đổi về sau; nâng cấp nếu cần so khác timezone qua DST.
        var shift = 0;
        if (a.TimeZoneId != b.TimeZoneId)
        {
            var first = DateOnly.FromDayNumber(Math.Max(aStart, bStart));
            shift = (int)(OffsetAt(a, first) - OffsetAt(b, first)).TotalMinutes;
        }

        var aFrom = a.TimeOfDay.Hour * 60 + a.TimeOfDay.Minute;
        var bFrom = b.TimeOfDay.Hour * 60 + b.TimeOfDay.Minute + shift;

        // Grace ≤ 120 phút và lệch timezone ≤ 26 giờ nên chỉ cần xét lệch ngày k trong [-3, 3].
        for (var k = -3; k <= 3; k++)
        {
            var bFromShifted = bFrom + k * 1440;
            if (aFrom > bFromShifted + b.GracePeriodMinutes || bFromShifted > aFrom + a.GracePeriodMinutes)
            {
                continue;
            }

            // d thuộc khung của A và d + k thuộc khung của B; 7 ngày đầu đủ phủ mọi thứ trong tuần.
            var lo = Math.Max(aStart, bStart - k);
            var hi = Math.Min(Math.Min(aEnd, bEnd - k), lo + 6);
            for (var d = lo; d <= hi; d++)
            {
                if (a.DaysOfWeek.Contains(IsoDay(d)) && b.DaysOfWeek.Contains(IsoDay(d + k)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static TimeSpan OffsetAt(OverlapWindow window, DateOnly date) =>
        TimeZoneInfo.FindSystemTimeZoneById(window.TimeZoneId).GetUtcOffset(date.ToDateTime(window.TimeOfDay));

    // DayNumber 0 (0001-01-01) là thứ Hai → ISO 1..7.
    private static int IsoDay(int dayNumber) => dayNumber % 7 + 1;

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
