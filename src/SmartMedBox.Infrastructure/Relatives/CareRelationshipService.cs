using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Infrastructure.Relatives;

public enum RelationshipOutcome
{
    Success,
    Forbidden,
    NotFound,
}

public sealed record RelationshipView(
    Guid Id,
    Guid DeviceId,
    string SerialNumber,
    Guid RelativeUserId,
    string RelativeEmail,
    RelationshipStatus Status,
    DateTimeOffset AcceptedAt);

/// <summary>Quan hệ người thân (API_CONTRACT §6.5–6.6). <c>userId</c> luôn lấy từ phiên đăng nhập.</summary>
public sealed class CareRelationshipService(AppDbContext db, DeviceAccessService access, IClock clock)
{
    /// <summary>
    /// Chủ hộp thấy mọi quan hệ (kể cả đã thu hồi) của hộp mình; người thân chỉ thấy quan hệ Active của chính mình,
    /// nên không bao giờ thấy email người thân khác. Trả null nếu <paramref name="deviceId"/> ngoài quyền.
    /// </summary>
    public async Task<IReadOnlyList<RelationshipView>?> ListAsync(
        Guid userId,
        Guid? deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId is not null && !await access.CanAsync(userId, deviceId.Value, DeviceOperation.Read, cancellationToken))
        {
            return null;
        }

        return await (
                from r in db.CareRelationships
                join d in db.Devices on r.DeviceId equals d.Id
                join u in db.ApplicationUsers on r.RelativeUserId equals u.Id
                where d.OwnerUserId == userId || (r.RelativeUserId == userId && r.Status == RelationshipStatus.Active)
                where deviceId == null || r.DeviceId == deviceId
                orderby r.AcceptedAt descending
                select new RelationshipView(r.Id, r.DeviceId, d.SerialNumber, r.RelativeUserId, u.Email, r.Status, r.AcceptedAt))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>Chủ hộp thu hồi hoặc người thân tự ngừng theo dõi; giữ hàng làm lịch sử (append-only).</summary>
    public async Task<RelationshipOutcome> RevokeAsync(Guid userId, Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var relationship = await db.CareRelationships
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return RelationshipOutcome.NotFound;
        }

        var isOwner = await access.CanAsync(userId, relationship.DeviceId, DeviceOperation.Manage, cancellationToken);
        if (!isOwner && relationship.RelativeUserId != userId)
        {
            return RelationshipOutcome.Forbidden;
        }

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Đã thu hồi trước đó (hoặc request đồng thời thắng trước) → không còn quan hệ Active để thu hồi.
        var revoked = await db.CareRelationships
            .Where(x => x.Id == relationshipId && x.Status == RelationshipStatus.Active)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.Status, RelationshipStatus.Revoked).SetProperty(x => x.RevokedAt, now),
                cancellationToken);
        if (revoked == 0)
        {
            return RelationshipOutcome.NotFound;
        }

        AuditLogWriter.Add(db, now, AuditActions.RelationshipRevoked, nameof(CareRelationship),
            relationshipId.ToString(), userId, relationship.DeviceId, isOwner ? "by=owner" : "by=relative");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RelationshipOutcome.Success;
    }
}
