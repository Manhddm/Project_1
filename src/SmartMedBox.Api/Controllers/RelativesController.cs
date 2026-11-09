using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Relatives;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/relatives")]
public sealed class RelativesController(
    RelativeInvitationService invitations,
    CareRelationshipService relationships,
    IClock clock) : ControllerBase
{
    [HttpGet("relationships")]
    public async Task<IActionResult> ListRelationships([FromQuery] Guid? deviceId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var items = await relationships.ListAsync(userId, deviceId, cancellationToken);
        return items is null
            ? Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden)
            : Ok(new ItemsResponse<RelationshipDto>(items
                .Select(x => new RelationshipDto(
                    x.Id, x.DeviceId, x.SerialNumber, x.RelativeUserId, x.RelativeEmail, x.Status.ToString(), x.AcceptedAt))
                .ToList()));
    }

    [HttpDelete("relationships/{id:guid}")]
    public async Task<IActionResult> DeleteRelationship(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        return await relationships.RevokeAsync(userId, id, cancellationToken) switch
        {
            RelationshipOutcome.Success => NoContent(),
            RelationshipOutcome.Forbidden => Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
            _ => Fail(StatusCodes.Status404NotFound, ErrorCodes.RelationshipNotFound),
        };
    }

    [HttpPost("invitations")]
    [EnableRateLimiting(RateLimitPolicies.InvitationCreate)]
    public async Task<IActionResult> CreateInvitation(
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var result = await invitations.CreateAsync(userId, request.Email, cancellationToken);
        return result.Outcome switch
        {
            InvitationOutcome.Success => StatusCode(StatusCodes.Status201Created, ToDto(result.Invitation!)),
            InvitationOutcome.InviteeIsOwner => ApiProblemResults.Validation(HttpContext, "email", "Không thể tự mời chính mình."),
            _ => ToProblem(result.Outcome),
        };
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> ListInvitations([FromQuery] Guid? deviceId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var items = await invitations.ListAsync(userId, deviceId, cancellationToken);
        return items is null
            ? ToProblem(InvitationOutcome.Forbidden)
            : Ok(new ItemsResponse<InvitationDto>(items.Select(ToDto).ToList()));
    }

    [HttpPost("invitations/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var outcome = await invitations.RevokeAsync(userId, id, cancellationToken);
        return outcome == InvitationOutcome.Success ? NoContent() : ToProblem(outcome);
    }

    [HttpPost("invitations/accept")]
    [EnableRateLimiting(RateLimitPolicies.InvitationAccept)]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Fail(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        }

        var result = await invitations.AcceptAsync(userId, request.Token, cancellationToken);
        if (result.Outcome != InvitationOutcome.Success)
        {
            return ToProblem(result.Outcome);
        }

        var relationship = result.Relationship!;
        return Ok(new AcceptInvitationResponse(
            relationship.Id,
            relationship.DeviceId,
            nameof(InvitationStatus.Accepted),
            relationship.AcceptedAt));
    }

    // Pending đã quá hạn hiển thị Expired dù hàng chưa được cập nhật.
    private InvitationDto ToDto(Domain.Entities.RelativeInvitation x) => new(
        x.Id,
        x.InviteeEmail,
        (x.Status == InvitationStatus.Pending && x.ExpiresAt <= clock.UtcNow ? InvitationStatus.Expired : x.Status).ToString(),
        x.ExpiresAt);

    private IActionResult Fail(int status, string code) => ApiProblemResults.Create(HttpContext, status, code);

    private IActionResult ToProblem(InvitationOutcome outcome) => outcome switch
    {
        InvitationOutcome.Forbidden => Fail(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden),
        InvitationOutcome.AlreadyPending => Fail(StatusCodes.Status409Conflict, ErrorCodes.InviteAlreadyPending),
        InvitationOutcome.RelationshipExists => Fail(StatusCodes.Status409Conflict, ErrorCodes.RelationshipExists),
        InvitationOutcome.NotFound => Fail(StatusCodes.Status404NotFound, ErrorCodes.InviteNotFound),
        InvitationOutcome.NotPending => Fail(StatusCodes.Status409Conflict, ErrorCodes.InviteNotPending),
        InvitationOutcome.Expired => Fail(StatusCodes.Status410Gone, ErrorCodes.InviteExpired),
        InvitationOutcome.EmailMismatch => Fail(StatusCodes.Status403Forbidden, ErrorCodes.InviteEmailMismatch),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
