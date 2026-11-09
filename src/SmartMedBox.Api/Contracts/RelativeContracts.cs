using System.ComponentModel.DataAnnotations;

namespace SmartMedBox.Api.Contracts;

public sealed class CreateInvitationRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;
}

public sealed record InvitationDto(Guid Id, string InviteeEmail, string Status, DateTimeOffset ExpiresAt);

public sealed record ItemsResponse<T>(IReadOnlyList<T> Items);

public sealed class AcceptInvitationRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

public sealed record AcceptInvitationResponse(Guid RelationshipId, Guid DeviceId, string Status, DateTimeOffset AcceptedAt);
