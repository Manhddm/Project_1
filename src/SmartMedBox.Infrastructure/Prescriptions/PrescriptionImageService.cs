using Microsoft.EntityFrameworkCore;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Prescriptions;

/// <summary>
/// Ảnh đơn thuốc (P6-*): CSDL chỉ giữ metadata + storage key, nội dung nằm trong <c>IFileStorage</c> private
/// (local disk theo [ĐỀ XUẤT]; D-04 vẫn TBD). Chỉ chủ hộp được thao tác; người thân/người ngoài bị từ chối.
/// </summary>
public sealed class PrescriptionImageService(AppDbContext db)
{
    /// <summary>Hộp mà người dùng sở hữu; null nếu không phải chủ hộp nào (người thân hoặc người ngoài).</summary>
    public Task<Guid?> GetOwnedDeviceIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Devices
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
