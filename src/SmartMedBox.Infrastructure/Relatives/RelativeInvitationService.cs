using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.Infrastructure.Relatives;

public enum InvitationOutcome
{
    Success,
    Forbidden,
    InviteeIsOwner,
    AlreadyPending,
    RelationshipExists,
    NotFound,
    NotPending,
    Expired,
    EmailMismatch,
}

public sealed record InvitationResult(InvitationOutcome Outcome, RelativeInvitation? Invitation = null);

public sealed record AcceptInvitationResult(InvitationOutcome Outcome, CareRelationship? Relationship = null);

/// <summary>Lời mời người thân (API_CONTRACT §6.1–6.4). <c>userId</c> luôn lấy từ phiên đăng nhập.</summary>
public sealed class RelativeInvitationService(
    AppDbContext db,
    DeviceAccessService access,
    ITokenHasher tokenHasher,
    IEmailSender emailSender,
    IClock clock)
{
    public async Task<InvitationResult> CreateAsync(Guid ownerUserId, string email, CancellationToken cancellationToken = default)
    {
        var deviceId = await FindOwnedDeviceIdAsync(ownerUserId, cancellationToken);
        if (deviceId is null || !await access.CanAsync(ownerUserId, deviceId.Value, DeviceOperation.Manage, cancellationToken))
        {
            return new InvitationResult(InvitationOutcome.Forbidden);
        }

        var inviteeEmail = EmailNormalizer.Normalize(email);
        var ownerEmail = await db.ApplicationUsers
            .Where(x => x.Id == ownerUserId)
            .Select(x => x.Email)
            .SingleAsync(cancellationToken);
        if (ownerEmail == inviteeEmail)
        {
            return new InvitationResult(InvitationOutcome.InviteeIsOwner);
        }

        var alreadyRelated = await db.CareRelationships
            .Where(r => r.DeviceId == deviceId && r.Status == RelationshipStatus.Active)
            .Join(db.ApplicationUsers, r => r.RelativeUserId, u => u.Id, (_, u) => u.Email)
            .AnyAsync(e => e == inviteeEmail, cancellationToken);
        if (alreadyRelated)
        {
            return new InvitationResult(InvitationOutcome.RelationshipExists);
        }

        var now = clock.UtcNow;

        // Lời mời Pending đã quá hạn vẫn chiếm filtered unique index; chuyển sang Expired để cho phép mời lại.
        await db.RelativeInvitations
            .Where(x => x.DeviceId == deviceId && x.InviteeEmail == inviteeEmail
                && x.Status == InvitationStatus.Pending && x.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvitationStatus.Expired), cancellationToken);

        var token = SecureToken.Create();
        var invitation = new RelativeInvitation
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId.Value,
            InviteeEmail = inviteeEmail,
            TokenHash = tokenHasher.Hash(token),
            Status = InvitationStatus.Pending,
            ExpiresAt = now.Add(AuthTokenLifetime.RelativeInvitation),
        };
        db.RelativeInvitations.Add(invitation);
        AuditLogWriter.Add(db, now, AuditActions.InvitationCreated, nameof(RelativeInvitation),
            invitation.Id.ToString(), ownerUserId, deviceId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new InvitationResult(InvitationOutcome.AlreadyPending);
        }

        // Người chưa có tài khoản vẫn nhận email: đăng ký + xác minh đúng email này rồi mới accept (§6.4).
        await emailSender.SendAsync(
            EmailTemplates.RelativeInvitation(inviteeEmail, token, AuthTokenLifetime.RelativeInvitation),
            cancellationToken);

        return new InvitationResult(InvitationOutcome.Success, invitation);
    }

    /// <summary>Trả null nếu người gọi không có quyền quản lý hộp.</summary>
    public async Task<IReadOnlyList<RelativeInvitation>?> ListAsync(
        Guid ownerUserId,
        Guid? deviceId,
        CancellationToken cancellationToken = default)
    {
        deviceId ??= await FindOwnedDeviceIdAsync(ownerUserId, cancellationToken);
        if (deviceId is null || !await access.CanAsync(ownerUserId, deviceId.Value, DeviceOperation.Manage, cancellationToken))
        {
            return null;
        }

        return await db.RelativeInvitations
            .AsNoTracking()
            .Where(x => x.DeviceId == deviceId)
            .OrderByDescending(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<InvitationOutcome> RevokeAsync(Guid ownerUserId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var deviceId = await db.RelativeInvitations
            .Where(x => x.Id == invitationId)
            .Select(x => (Guid?)x.DeviceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (deviceId is null)
        {
            return InvitationOutcome.NotFound;
        }

        if (!await access.CanAsync(ownerUserId, deviceId.Value, DeviceOperation.Manage, cancellationToken))
        {
            return InvitationOutcome.Forbidden;
        }

        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Điều kiện Status = Pending giữ cho revoke và accept đồng thời chỉ một bên thắng.
        var revoked = await db.RelativeInvitations
            .Where(x => x.Id == invitationId && x.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvitationStatus.Revoked), cancellationToken);
        if (revoked == 0)
        {
            return InvitationOutcome.NotPending;
        }

        AuditLogWriter.Add(db, now, AuditActions.InvitationRevoked, nameof(RelativeInvitation),
            invitationId.ToString(), ownerUserId, deviceId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InvitationOutcome.Success;
    }

    public async Task<AcceptInvitationResult> AcceptAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var normalized = token.Trim();
        if (normalized.Length is 0 or > 256)
        {
            return new AcceptInvitationResult(InvitationOutcome.NotFound);
        }

        var hash = tokenHasher.Hash(normalized);
        var invitation = await db.RelativeInvitations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (invitation is null)
        {
            return new AcceptInvitationResult(InvitationOutcome.NotFound);
        }

        var userEmail = await db.ApplicationUsers
            .Where(x => x.Id == userId && !x.IsDeleted)
            .Select(x => x.Email)
            .SingleOrDefaultAsync(cancellationToken);
        if (userEmail != invitation.InviteeEmail)
        {
            return new AcceptInvitationResult(InvitationOutcome.EmailMismatch);
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return new AcceptInvitationResult(InvitationOutcome.NotPending);
        }

        var now = clock.UtcNow;
        if (invitation.ExpiresAt <= now)
        {
            return new AcceptInvitationResult(InvitationOutcome.Expired);
        }

        if (await db.CareRelationships.AnyAsync(
                x => x.DeviceId == invitation.DeviceId && x.RelativeUserId == userId && x.Status == RelationshipStatus.Active,
                cancellationToken))
        {
            return new AcceptInvitationResult(InvitationOutcome.RelationshipExists);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Tiêu thụ token nguyên tử: request đồng thời thứ hai thấy Status đã đổi và nhận 0 dòng.
        var consumed = await db.RelativeInvitations
            .Where(x => x.Id == invitation.Id && x.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, InvitationStatus.Accepted), cancellationToken);
        if (consumed == 0)
        {
            return new AcceptInvitationResult(InvitationOutcome.NotPending);
        }

        var relationship = new CareRelationship
        {
            Id = Guid.NewGuid(),
            DeviceId = invitation.DeviceId,
            RelativeUserId = userId,
            Status = RelationshipStatus.Active,
            AcceptedAt = now,
        };
        db.CareRelationships.Add(relationship);
        AuditLogWriter.Add(db, now, AuditActions.InvitationAccepted, nameof(RelativeInvitation),
            invitation.Id.ToString(), userId, invitation.DeviceId, $"relationshipId={relationship.Id}");

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new AcceptInvitationResult(InvitationOutcome.RelationshipExists);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AcceptInvitationResult(InvitationOutcome.Success, relationship);
    }

    private Task<Guid?> FindOwnedDeviceIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Devices
            .Where(x => x.OwnerUserId == userId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
