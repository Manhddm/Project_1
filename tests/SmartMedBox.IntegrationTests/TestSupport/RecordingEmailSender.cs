using System.Collections.Concurrent;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.IntegrationTests.TestSupport;

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage SingleTo(string email) =>
        _messages.Single(message => string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase));

    public EmailMessage LastTo(string email) =>
        _messages.Last(message => string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase));
}