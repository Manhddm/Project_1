using System.Security.Cryptography;

namespace SmartMedBox.Domain.Accounts;

public static class SecureToken
{
    public const int ByteLength = 32;

    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}