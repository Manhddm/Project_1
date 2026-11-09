using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

public enum PairingOutcome
{
    Success,
    EmailNotVerified,
    CodeInvalid,
    CodeUsed,
    CodeExpired,
    UserAlreadyOwnsDevice,
    DeviceAlreadyPaired,
}

public sealed record OwnedCompartment(int SlotNumber, string Name, bool IsEnabled);

/// <summary>Hộp nhìn từ phía chủ: không có DeviceId kỹ thuật, khóa hay hash.</summary>
public sealed record OwnedDevice(
    Guid Id,
    string SerialNumber,
    int ScheduleVersion,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<OwnedCompartment> Compartments);

public sealed record PairingResult(PairingOutcome Outcome, OwnedDevice? Device = null, DateTimeOffset? PairedAt = null);

/// <summary>Ghép hộp bằng pairing code (P2-04, AC-08). <c>userId</c> luôn lấy từ phiên đăng nhập.</summary>
public sealed class DeviceOwnershipService(AppDbContext db, ITokenHasher hasher, IClock clock)
{
    public async Task<PairingResult> PairAsync(Guid userId, string? code, CancellationToken cancellationToken = default)
    {
        var emailVerified = await db.ApplicationUsers
            .Where(x => x.Id == userId && !x.IsDeleted)
            .Select(x => x.EmailVerified)
            .SingleOrDefaultAsync(cancellationToken);
        if (!emailVerified)
        {
            return new PairingResult(PairingOutcome.EmailNotVerified);
        }

        // Mã sai định dạng và mã không tồn tại trả cùng kết quả để không dò được thiết bị.
        if (!PairingCode.TryNormalize(code, out var normalized))
        {
            return new PairingResult(PairingOutcome.CodeInvalid);
        }

        var hash = hasher.Hash(normalized);
        var pairingCode = await db.DevicePairingCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CodeHash == hash, cancellationToken);
        if (pairingCode is null)
        {
            return new PairingResult(PairingOutcome.CodeInvalid);
        }

        // Mã đã dùng xét trước để quét lại (kể cả bởi chính chủ vừa ghép) luôn nhận PAIRING_CODE_USED.
        if (pairingCode.UsedAt is not null)
        {
            return new PairingResult(PairingOutcome.CodeUsed);
        }

        var now = clock.UtcNow;
        if (pairingCode.ExpiresAt <= now)
        {
            return new PairingResult(PairingOutcome.CodeExpired);
        }

        if (await db.Devices.AnyAsync(x => x.OwnerUserId == userId, cancellationToken))
        {
            return new PairingResult(PairingOutcome.UserAlreadyOwnsDevice);
        }

        if (await db.Devices.AnyAsync(x => x.Id == pairingCode.DeviceId && x.OwnerUserId != null, cancellationToken))
        {
            return new PairingResult(PairingOutcome.DeviceAlreadyPaired);
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Cập nhật có điều kiện: request đồng thời thứ hai chờ khóa dòng rồi thấy UsedAt đã set → 0 dòng.
            var consumed = await db.DevicePairingCodes
                .Where(x => x.Id == pairingCode.Id && x.UsedAt == null && x.ExpiresAt > now)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.UsedAt, now).SetProperty(x => x.UsedByUserId, userId),
                    cancellationToken);
            if (consumed == 0)
            {
                return new PairingResult(PairingOutcome.CodeUsed);
            }

            int assigned;
            try
            {
                assigned = await db.Devices
                    .Where(x => x.Id == pairingCode.DeviceId && x.OwnerUserId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.OwnerUserId, userId), cancellationToken);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                // Index unique filtered trên OwnerUserId: cùng người dùng ghép song song hộp khác đã thắng.
                return new PairingResult(PairingOutcome.UserAlreadyOwnsDevice);
            }

            if (assigned == 0)
            {
                return new PairingResult(PairingOutcome.DeviceAlreadyPaired);
            }

            AuditLogWriter.Add(db, now, AuditActions.DevicePaired, nameof(Device),
                pairingCode.DeviceId.ToString(), userId, pairingCode.DeviceId);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new PairingResult(PairingOutcome.Success, await LoadAsync(pairingCode.DeviceId, cancellationToken), now);
    }

    private async Task<OwnedDevice> LoadAsync(Guid deviceKey, CancellationToken cancellationToken)
    {
        var device = await db.Devices
            .AsNoTracking()
            .Where(x => x.Id == deviceKey)
            .Select(x => new { x.Id, x.SerialNumber, x.ScheduleVersion, x.LastSeenAt })
            .SingleAsync(cancellationToken);
        var compartments = await db.Compartments
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceKey)
            .OrderBy(x => x.SlotNumber)
            .Select(x => new OwnedCompartment(x.SlotNumber, x.Name, x.IsEnabled))
            .ToListAsync(cancellationToken);
        return new OwnedDevice(device.Id, device.SerialNumber, device.ScheduleVersion, device.LastSeenAt, compartments);
    }

    private static bool IsUniqueViolation(Exception exception) =>
        (exception as SqlException ?? exception.InnerException as SqlException) is { Number: 2601 or 2627 };
}
