using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartMedBox.Infrastructure.Email;

// D-03: SMTP via System.Net.Mail. Never log recipient or body.
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions _options;

    public SmtpEmailSender(IOptions<SmtpEmailOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public async Task<string> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var messageId = EmailHeaders.NewMessageId(_options.FromAddress!);
        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress!, _options.FromName is null ? null : EmailHeaders.SingleLine(_options.FromName), Encoding.UTF8),
            Subject = EmailHeaders.SingleLine(message.Subject),
            SubjectEncoding = Encoding.UTF8,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.To.Add(new MailAddress(EmailHeaders.SingleLine(message.To)));
        mail.Headers.Add("Message-ID", messageId);

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(_options.PickupDirectory))
        {
            var directory = Path.GetFullPath(_options.PickupDirectory);
            Directory.CreateDirectory(directory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = directory;
        }
        else
        {
            client.Host = _options.Host!;
            client.Port = _options.Port;
            client.EnableSsl = _options.EnableSsl;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            if (!string.IsNullOrEmpty(_options.UserName)) client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
        return messageId;
    }
}
