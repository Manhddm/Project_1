using System.Net.Mail;

namespace SmartMedBox.Infrastructure.Email;

/// <summary>Bound from <c>Email:Smtp</c>. UserName/Password only via user-secrets/env (D-03).</summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }

    /// <summary>Optional: write .eml files here instead of connecting (diagnostics/tests, no network).</summary>
    public string? PickupDirectory { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PickupDirectory) && string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("Email:Smtp:Host is required when Email:Provider is Smtp (see README).");
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException("Email:Smtp:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(FromAddress) || !MailAddress.TryCreate(FromAddress, out _))
            throw new InvalidOperationException("Email:Smtp:FromAddress must be a valid email address.");
        if (string.IsNullOrEmpty(UserName) != string.IsNullOrEmpty(Password))
            throw new InvalidOperationException("Email:Smtp:UserName and Email:Smtp:Password must be set together (user-secrets/env).");
    }
}
