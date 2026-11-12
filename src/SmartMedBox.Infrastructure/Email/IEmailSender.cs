namespace SmartMedBox.Infrastructure.Email;

public interface IEmailSender
{
    /// <summary>Sends the message and returns the server-generated <c>Message-ID</c> (stored as ProviderMessageId, D-03).</summary>
    Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public static class EmailHeaders
{
    public static string NewMessageId(string fromAddress)
    {
        var at = fromAddress.LastIndexOf('@');
        var domain = at >= 0 && at < fromAddress.Length - 1 ? fromAddress[(at + 1)..] : "localhost";
        return $"<{Guid.NewGuid():N}@{domain}>";
    }

    // Prevents header injection: a header value can never span lines.
    public static string SingleLine(string value) => value.ReplaceLineEndings(" ");
}
