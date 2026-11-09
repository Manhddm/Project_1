using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Auditing;

/// <summary>Tên hành động audit (AGENTS §8). Thêm hằng số mới ở đây khi có thao tác mới.</summary>
public static class AuditActions
{
    public const string ScheduleCreated = "ScheduleCreated";
    public const string ScheduleUpdated = "ScheduleUpdated";
    public const string ScheduleDeleted = "ScheduleDeleted";
    public const string InvitationCreated = "InvitationCreated";
    public const string InvitationRevoked = "InvitationRevoked";
    public const string InvitationAccepted = "InvitationAccepted";
    public const string RelationshipRevoked = "RelationshipRevoked";
    public const string DeviceProvisioned = "DeviceProvisioned";
    public const string PairingCodeIssued = "PairingCodeIssued";
}

public static class AuditLogWriter
{
    /// <summary>
    /// Thêm một dòng audit vào context hiện tại, không gọi SaveChanges — để audit nằm cùng transaction với thao tác.
    /// <paramref name="details"/> không được chứa secret, token, mật khẩu, mã ghép hoặc nội dung sức khỏe.
    /// </summary>
    public static void Add(
        AppDbContext db,
        DateTimeOffset occurredAt,
        string action,
        string entityType,
        string entityId,
        Guid? actorUserId,
        Guid? deviceId,
        string details = "") =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            DeviceId = deviceId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = occurredAt,
            Details = details,
        });
}
