using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.Infrastructure.Identity;

public enum LoginOutcome
{
    Succeeded,
    InvalidEmail,
    InvalidCredentials,
    EmailNotVerified,
}

public sealed record LoginResult(
    LoginOutcome Outcome,
    ApplicationUser? User = null,
    string? SessionId = null,
    DateTimeOffset ExpiresAt = default);

/// <summary>Kiểm tra thông tin đăng nhập và tạo phiên server-side; dùng chung cho API và Razor UI.</summary>
public sealed class LoginService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IUserSessionStore sessions,
    IClock clock)
{
    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        string normalizedEmail;
        try
        {
            normalizedEmail = EmailNormalizer.Normalize(email);
        }
        catch (ArgumentException)
        {
            return new LoginResult(LoginOutcome.InvalidEmail);
        }

        var user = await db.ApplicationUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Email == normalizedEmail && !x.IsDeleted, cancellationToken);

        // Email không tồn tại và sai mật khẩu cùng một kết quả để không liệt kê tài khoản.
        if (user is null || !passwordHasher.Verify(user.PasswordHash, password))
        {
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (!user.EmailVerified)
        {
            return new LoginResult(LoginOutcome.EmailNotVerified);
        }

        var expiresAt = clock.UtcNow.Add(UserSessionDefaults.Lifetime);
        return new LoginResult(LoginOutcome.Succeeded, user, sessions.Create(user.Id, expiresAt), expiresAt);
    }
}
