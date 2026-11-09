using Microsoft.AspNetCore.Identity;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;

namespace SmartMedBox.Infrastructure.Identity;

public sealed class PasswordHasherService : IPasswordHasher
{
    private static readonly ApplicationUser HasherUser = new();
    private readonly PasswordHasher<ApplicationUser> _hasher = new();

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }

        return _hasher.HashPassword(HasherUser, password);
    }

    public bool Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            return _hasher.VerifyHashedPassword(HasherUser, passwordHash, password)
                != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // Hash lưu trong DB bị hỏng/không đúng định dạng: coi như xác thực thất bại thay vì 500.
            return false;
        }
    }
}
