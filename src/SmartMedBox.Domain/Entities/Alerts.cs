using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Domain.Entities;

public sealed class Alert
{
    public Guid Id { get; set; }
    public Guid DoseInstanceId { get; set; }
    public AlertType AlertType { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class AlertRecipient
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public Guid RecipientUserId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class EmailDelivery
{
    public Guid Id { get; set; }
    public Guid AlertRecipientId { get; set; }
    public int AttemptNumber { get; set; }
    public EmailDeliveryStatus Status { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
