using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.Infrastructure.Email;

public sealed class FileEmailSender(IOptions<EmailSinkOptions> options, IClock clock) : IEmailSender
{
    private readonly string _directoryPath = options.Value.DirectoryPath;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var sentAt = clock.UtcNow;
        var fileName = $"{sentAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.eml";

        await File.WriteAllTextAsync(
            Path.Combine(_directoryPath, fileName),
            BuildMessage(message, sentAt),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static string BuildMessage(EmailMessage message, DateTimeOffset sentAt)
    {
        var builder = new StringBuilder();
        builder.Append("From: no-reply@smartmedbox.local\n");
        builder.Append("To: ").Append(SingleLine(message.To)).Append('\n');
        builder.Append("Subject: ").Append(SingleLine(message.Subject)).Append('\n');
        builder.Append("Date: ").Append(sentAt.UtcDateTime.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("MIME-Version: 1.0\n");
        builder.Append("Content-Type: text/plain; charset=utf-8\n");
        builder.Append('\n');
        builder.Append(message.Body);
        return builder.ToString();
    }

    private static string SingleLine(string value) => value.ReplaceLineEndings(" ");
}
