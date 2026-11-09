using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.Infrastructure.Identity;

public enum RegistrationOutcome
{
    Created,
    EmailTaken,
}

public sealed record RegistrationResult(RegistrationOutcome Outcome, ApplicationUser? User);

public enum EmailVerificationOutcome
{
    Verified,
    InvalidToken,
    Expired,
}

public sealed class RegistrationService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenHasher tokenHasher,
    IEmailSender emailSender,
    IClock clock)
{
    public async Task<RegistrationResult> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = EmailNormalizer.Normalize(email);
        if (await db.ApplicationUsers.AnyAsync(x => x.Email == normalizedEmail, cancellationToken))
        {
            return new RegistrationResult(RegistrationOutcome.EmailTaken, null);
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.Hash(password),
            EmailVerified = false,
            CreatedAt = now,
        };

        db.ApplicationUsers.Add(user);
        var token = IssueEmailVerificationToken(user.Id, now);

        try
        {
            // Một SaveChanges = một transaction: user và token xác minh cùng tồn tại hoặc cùng rollback.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return new RegistrationResult(RegistrationOutcome.EmailTaken, null);
        }

        await emailSender.SendAsync(
            EmailTemplates.EmailVerification(user.Email, token, AuthTokenLifetime.EmailVerification),
            cancellationToken);

        return new RegistrationResult(RegistrationOutcome.Created, user);
    }

    public async Task<EmailVerificationOutcome> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var normalized = token.Trim();
        if (normalized.Length == 0)
        {
            return EmailVerificationOutcome.InvalidToken;
        }

        var hash = tokenHasher.Hash(normalized);
        var authToken = await db.AuthTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TokenHash == hash && x.Purpose == AuthTokenPurpose.EmailVerification,
                cancellationToken);

        if (authToken is null || authToken.UsedAt is not null)
        {
            return EmailVerificationOutcome.InvalidToken;
        }

        var now = clock.UtcNow;
        if (authToken.ExpiresAt <= now)
        {
            return EmailVerificationOutcome.Expired;
        }

        var user = await db.ApplicationUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == authToken.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
        {
            return EmailVerificationOutcome.InvalidToken;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Điều kiện UsedAt IS NULL biến việc tiêu thụ token thành nguyên tử khi có request đồng thời.
        var consumed = await db.AuthTokens
            .Where(x => x.Id == authToken.Id && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);
        if (consumed == 0)
        {
            return EmailVerificationOutcome.InvalidToken;
        }

        await db.ApplicationUsers
            .Where(x => x.Id == authToken.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EmailVerified, true), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return EmailVerificationOutcome.Verified;
    }

    public async Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default)
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
        if (user is null || user.EmailVerified)
        {
            return;
        }

        var now = clock.UtcNow;

        // Gửi lại làm token cũ mất hiệu lực: chỉ token mới nhất dùng được.
        await db.AuthTokens
            .Where(x => x.UserId == user.Id
                && x.Purpose == AuthTokenPurpose.EmailVerification
                && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);

        var token = IssueEmailVerificationToken(user.Id, now);
        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            EmailTemplates.EmailVerification(user.Email, token, AuthTokenLifetime.EmailVerification),
            cancellationToken);
    }

    private string IssueEmailVerificationToken(Guid userId, DateTimeOffset now)
    {
        var token = SecureToken.Create();
        db.AuthTokens.Add(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = AuthTokenPurpose.EmailVerification,
            TokenHash = tokenHasher.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(AuthTokenLifetime.EmailVerification),
        });

        return token;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}