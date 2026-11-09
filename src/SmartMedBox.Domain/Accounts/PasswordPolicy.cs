namespace SmartMedBox.Domain.Accounts;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsValid(string? password)
    {
        if (password is null || password.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var character in password)
        {
            hasLetter |= char.IsLetter(character);
            hasDigit |= char.IsDigit(character);
            if (hasLetter && hasDigit)
            {
                return true;
            }
        }

        return false;
    }
}