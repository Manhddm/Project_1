using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Domain.Entities;

public sealed class ApplicationUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class CareRelationship
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid RelativeUserId { get; set; }
    public RelationshipStatus Status { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class RelativeInvitation
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string InviteeEmail { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public InvitationStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class PrescriptionImage
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? DeviceId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Details { get; set; } = string.Empty;
}
