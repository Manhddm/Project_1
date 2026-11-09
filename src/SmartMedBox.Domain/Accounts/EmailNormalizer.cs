namespace SmartMedBox.Domain.Accounts;

public static class EmailNormalizer
{
    public const int MaxLength = 320;

    public static string Normalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"Email must not exceed {MaxLength} characters.", nameof(email));
        }

        return normalized;
    }
}
