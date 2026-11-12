using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.UnitTests.Email;

// Real SmtpClient path with SpecifiedPickupDirectory: no network, no SMTP server.
public sealed class SmtpEmailSenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "smartmedbox-smtp-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendAsync_WritesMessageWithHeadersAndReturnsMessageId()
    {
        var messageId = await CreateSender().SendAsync(new EmailMessage("owner@example.com", "Cảnh báo lần uống", "Ngăn Sáng đã mở."));

        Assert.Matches("^<[0-9a-f]{32}@smartmedbox.example>$", messageId);
        var content = await ReadSingleAsync();
        Assert.Contains($"Message-ID: {messageId}", content, StringComparison.Ordinal);
        Assert.Contains("To: owner@example.com", content, StringComparison.Ordinal);
        Assert.Contains("no-reply@smartmedbox.example", content, StringComparison.Ordinal);
        Assert.Contains("Subject: =?utf-8?", content, StringComparison.OrdinalIgnoreCase); // non-ASCII subject is encoded
    }

    [Fact]
    public async Task SendAsync_TwoSends_GenerateDistinctMessageIds()
    {
        var sender = CreateSender();
        var first = await sender.SendAsync(new EmailMessage("a@example.com", "s", "b"));
        var second = await sender.SendAsync(new EmailMessage("a@example.com", "s", "b"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task SendAsync_WithNewlinesInSubject_DoesNotInjectHeaders()
    {
        await CreateSender().SendAsync(new EmailMessage("owner@example.com", "Alert\r\nBcc: attacker@example.com", "body"));

        var content = await ReadSingleAsync();
        Assert.DoesNotContain("\nBcc:", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Subject: Alert Bcc: attacker@example.com", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_MissingHost_FailsFast()
    {
        var options = new SmtpEmailOptions { FromAddress = "no-reply@smartmedbox.example" };

        Assert.Throws<InvalidOperationException>(() => new SmtpEmailSender(Options.Create(options)));
    }

    [Fact]
    public void Constructor_InvalidFromAddress_FailsFast()
    {
        var options = new SmtpEmailOptions { Host = "smtp.example.com", FromAddress = "not an address" };

        Assert.Throws<InvalidOperationException>(() => new SmtpEmailSender(Options.Create(options)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private SmtpEmailSender CreateSender() => new(Options.Create(new SmtpEmailOptions
    {
        FromAddress = "no-reply@smartmedbox.example", FromName = "Hộp thuốc thông minh", PickupDirectory = _directory,
    }));

    private async Task<string> ReadSingleAsync() => await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory, "*.eml")));
}
