using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

public sealed record IssuedPairingCode(string Code, DateTimeOffset ExpiresAt);

/// <summary>Kết quả provision. <see cref="DeviceApiKey"/> và mã ghép thô chỉ có ở đây, không bao giờ lưu.</summary>
public sealed record ProvisioningResult(ProvisionedDevice Provisioned, string DeviceApiKey, IssuedPairingCode PairingCode);

/// <summary>Provision thiết bị và cấp pairing code (P2-02, D-05/D-07/D-08) — chỉ dùng từ CLI vận hành.</summary>
public sealed class DeviceOnboardingService(AppDbContext db, ITokenHasher hasher, IClock clock)
{
    public async Task<ProvisioningResult> ProvisionAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        var apiKey = DeviceApiKey.Create();
        var deviceId = "SMB-" + RandomNumberGenerator.GetString(PairingCode.Alphabet, 8);
        var provisioned = DeviceProvisioning.Create(deviceId, serialNumber, hasher.Hash(apiKey));
        var now = clock.UtcNow;

        db.Devices.Add(provisioned.Device);
        db.Compartments.AddRange(provisioned.Compartments);
        var code = AddPairingCode(provisioned.Device.Id, now);
        AuditLogWriter.Add(db, now, AuditActions.DeviceProvisioned, nameof(Device),
            provisioned.Device.Id.ToString(), actorUserId: null, provisioned.Device.Id);
        await db.SaveChangesAsync(cancellationToken);

        return new ProvisioningResult(provisioned, apiKey, code);
    }

    /// <summary>Cấp mã mới cho hộp chưa có chủ; mã cũ còn hạn bị vô hiệu. Trả null nếu không tồn tại hoặc đã ghép.</summary>
    public async Task<IssuedPairingCode?> IssuePairingCodeAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var device = await db.Devices.SingleOrDefaultAsync(x => x.DeviceId == deviceId, cancellationToken);
        if (device is null || device.OwnerUserId is not null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var active = await db.DevicePairingCodes
            .Where(x => x.DeviceId == device.Id && x.UsedAt == null && x.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var old in active)
        {
            old.ExpiresAt = now;
        }

        var code = AddPairingCode(device.Id, now);
        AuditLogWriter.Add(db, now, AuditActions.PairingCodeIssued, nameof(Device),
            device.Id.ToString(), actorUserId: null, device.Id);
        await db.SaveChangesAsync(cancellationToken);

        return code;
    }

    private IssuedPairingCode AddPairingCode(Guid deviceKey, DateTimeOffset now)
    {
        var code = PairingCode.Create();
        var expiresAt = now + PairingCode.Lifetime;
        db.DevicePairingCodes.Add(new DevicePairingCode
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceKey,
            CodeHash = hasher.Hash(code),
            ExpiresAt = expiresAt,
        });
        return new IssuedPairingCode(code, expiresAt);
    }
}
