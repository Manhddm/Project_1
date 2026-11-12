using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.Infrastructure.Email;

public sealed class FileEmailSender(IOptions<EmailSinkOptions> options, IClock clock) : IEmailSender
{
    private const string FromAddress = "no-reply@smartmedbox.local";
    private readonly string _directoryPath = options.Value.DirectoryPath;

    public async Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var sentAt = clock.UtcNow;
        var messageId = EmailHeaders.NewMessageId(FromAddress);
        var fileName = $"{sentAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.eml";

        await File.WriteAllTextAsync(
            Path.Combine(_directoryPath, fileName),
            BuildMessage(message, sentAt, messageId),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        return messageId;
    }

    private static string BuildMessage(EmailMessage message, DateTimeOffset sentAt, string messageId)
    {
        var builder = new StringBuilder();
        builder.Append("From: ").Append(FromAddress).Append('\n');
        builder.Append("To: ").Append(EmailHeaders.SingleLine(message.To)).Append('\n');
        builder.Append("Subject: ").Append(EmailHeaders.SingleLine(message.Subject)).Append('\n');
        builder.Append("Date: ").Append(sentAt.UtcDateTime.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("Message-ID: ").Append(messageId).Append('\n');
        builder.Append("MIME-Version: 1.0\n");
        builder.Append("Content-Type: text/plain; charset=utf-8\n");
        builder.Append('\n');
        builder.Append(message.Body);
        return builder.ToString();
    }
}
