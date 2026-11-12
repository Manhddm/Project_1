using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.UnitTests.TestDoubles;

namespace SmartMedBox.UnitTests.Email;

public sealed class FileEmailSenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "smartmedbox-email-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendAsync_WritesEmlFileToSink()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 29, 8, 30, 15, 123, TimeSpan.Zero));
        var sender = CreateSender(clock);
        var message = new EmailMessage(
            "owner@example.com",
            "Thông báo hộp thuốc",
            "Ngăn Sáng đã được mở trong cửa sổ nhắc.");

        var messageId = await sender.SendAsync(message);

        var file = Assert.Single(Directory.GetFiles(_directory, "*.eml"));
        Assert.Matches("^<[0-9a-f]{32}@smartmedbox.local>$", messageId);
        Assert.Contains($"Message-ID: {messageId}\n", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
        Assert.StartsWith("20261029T083015123Z-", Path.GetFileName(file), StringComparison.Ordinal);

        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("To: owner@example.com\n", content, StringComparison.Ordinal);
        Assert.Contains("Subject: Thông báo hộp thuốc\n", content, StringComparison.Ordinal);
        Assert.Contains("Ngăn Sáng đã được mở trong cửa sổ nhắc.", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithNewlinesInHeaders_KeepsSingleHeaderLine()
    {
        var sender = CreateSender(new FakeClock(DateTimeOffset.UnixEpoch));
        var message = new EmailMessage(
            "owner@example.com",
            "Thông báo\r\nBcc: attacker@example.com",
            "Nội dung");

        await sender.SendAsync(message);

        var file = Assert.Single(Directory.GetFiles(_directory, "*.eml"));
        var content = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("\nBcc: attacker@example.com\n", content, StringComparison.Ordinal);
        Assert.Contains("Subject: Thông báo Bcc: attacker@example.com\n", content, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private FileEmailSender CreateSender(IClock clock) =>
        new(Options.Create(new EmailSinkOptions { DirectoryPath = _directory }), clock);
}
