using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.Infrastructure.Identity;

public enum PasswordResetOutcome
{
    Reset,
    InvalidToken,
    Expired,
}

public sealed class PasswordResetService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    IEmailSender emailSender,
    IUserSessionStore sessions,
    IClock clock)
{
    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = EmailNormalizer.Normalize(email);
        }
        catch (ArgumentException)
        {
            return;
        }

        var user = await db.ApplicationUsers
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail && !x.IsDeleted, cancellationToken);
        if (user is null)
        {
            return;
        }

        var now = clock.UtcNow;

        // Chỉ token mới nhất còn hiệu lực.
        await db.AuthTokens
            .Where(x => x.UserId == user.Id
                && x.Purpose == AuthTokenPurpose.PasswordReset
                && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);

        var token = SecureToken.Create();
        db.AuthTokens.Add(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Purpose = AuthTokenPurpose.PasswordReset,
            TokenHash = tokenHasher.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(AuthTokenLifetime.PasswordReset),
        });
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            EmailTemplates.PasswordReset(user.Email, token, AuthTokenLifetime.PasswordReset),
            cancellationToken);
    }

    public async Task<PasswordResetOutcome> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var normalized = token.Trim();
        if (normalized.Length is 0 or > 256)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        var hash = tokenHasher.Hash(normalized);
        var authToken = await db.AuthTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TokenHash == hash && x.Purpose == AuthTokenPurpose.PasswordReset,
                cancellationToken);

        if (authToken is null || authToken.UsedAt is not null)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        var now = clock.UtcNow;
        if (authToken.ExpiresAt <= now)
        {
            return PasswordResetOutcome.Expired;
        }

        var user = await db.ApplicationUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == authToken.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        var passwordHash = passwordHasher.Hash(newPassword);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Điều kiện UsedAt IS NULL biến việc tiêu thụ token thành nguyên tử khi có request đồng thời.
        var consumed = await db.AuthTokens
            .Where(x => x.Id == authToken.Id && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return PasswordResetOutcome.InvalidToken;
        }

        await db.ApplicationUsers
            .Where(x => x.Id == authToken.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PasswordHash, passwordHash), cancellationToken);

        await db.AuthTokens
            .Where(x => x.UserId == authToken.UserId
                && x.Purpose == AuthTokenPurpose.PasswordReset
                && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Hợp đồng §3.8: đổi mật khẩu thu hồi mọi phiên cũ (D-02 không có refresh token).
        sessions.RevokeAllForUser(authToken.UserId);
        return PasswordResetOutcome.Reset;
    }
}
