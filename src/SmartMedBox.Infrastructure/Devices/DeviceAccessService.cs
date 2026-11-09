using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

/// <summary>
/// Xác định vai trò của người dùng đối với một hộp. <paramref name="userId"/> phải lấy từ phiên đăng nhập,
/// không lấy từ dữ liệu client gửi.
/// </summary>
public sealed class DeviceAccessService(AppDbContext db)
{
    public async Task<DeviceAccessRole> GetRoleAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var ownerUserId = await db.Devices
            .AsNoTracking()
            .Where(x => x.Id == deviceId)
            .Select(x => x.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);

        // Hộp không tồn tại hoặc chưa ghép: không ai có quyền, trả cùng kết quả để không lộ tồn tại.
        if (ownerUserId is null)
        {
            return DeviceAccessRole.None;
        }

        if (ownerUserId == userId)
        {
            return DeviceAccessRole.Owner;
        }

        var isRelative = await db.CareRelationships
            .AsNoTracking()
            .AnyAsync(
                x => x.DeviceId == deviceId && x.RelativeUserId == userId && x.Status == RelationshipStatus.Active,
                cancellationToken);

        return isRelative ? DeviceAccessRole.Relative : DeviceAccessRole.None;
    }

    public async Task<bool> CanAsync(
        Guid userId,
        Guid deviceId,
        DeviceOperation operation,
        CancellationToken cancellationToken = default) =>
        DeviceAccessPolicy.Allows(await GetRoleAsync(userId, deviceId, cancellationToken), operation);
}
