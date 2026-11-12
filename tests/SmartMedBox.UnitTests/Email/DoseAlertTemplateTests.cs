using SmartMedBox.Domain.Enums;
using SmartMedBox.Infrastructure.Email;

namespace SmartMedBox.UnitTests.Email;

public sealed class DoseAlertTemplateTests
{
    // 00:00 UTC = 07:00 in Asia/Ho_Chi_Minh.
    private static readonly DateTimeOffset ScheduledAt = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string[] Medicines = ["Paracetamol 500mg", "Vitamin C"];

    [Fact]
    public void DoseAlert_Overdue_RendersExactBody()
    {
        var message = EmailTemplates.DoseAlert("a@example.com", AlertType.OverdueUnconfirmed, Medicines, ScheduledAt, "Asia/Ho_Chi_Minh", 1);

        Assert.Equal("a@example.com", message.To);
        Assert.Equal("Cảnh báo lần uống từ Hộp thuốc thông minh", message.Subject);
        Assert.Equal(
            """
            Xin chào,

            Lần nhắc thuốc lúc 07:00 ngày 12/11/2026 ở ngăn Sáng (ngăn 1) đã quá hạn và chưa được xác nhận: nắp ngăn này chưa được mở trong thời gian chờ, trong khi hộp thuốc vẫn đang kết nối.

            Thuốc trong lần nhắc này:
            - Paracetamol 500mg
            - Vitamin C

            Hệ thống chỉ biết nắp ngăn đã được mở hay chưa, không xác nhận việc dùng thuốc. Vui lòng liên hệ người dùng hộp thuốc để kiểm tra.

            Email này được gửi cho chủ hộp và mọi người thân đang theo dõi hộp thuốc.
            """.ReplaceLineEndings("\n"), message.Body);
    }

    [Fact]
    public void DoseAlert_Offline_RendersExactBody()
    {
        var message = EmailTemplates.DoseAlert("a@example.com", AlertType.UnconfirmedOffline, Medicines, ScheduledAt.AddHours(12), "Asia/Ho_Chi_Minh", 3);

        Assert.Equal(
            """
            Xin chào,

            Lần nhắc thuốc lúc 19:00 ngày 12/11/2026 ở ngăn Tối (ngăn 3): không thể xác nhận do mất kết nối. Hộp thuốc không gửi được dữ liệu lên máy chủ khi hết thời gian chờ, nên hệ thống không biết nắp ngăn đã được mở hay chưa.

            Thuốc trong lần nhắc này:
            - Paracetamol 500mg
            - Vitamin C

            Hệ thống chỉ biết nắp ngăn đã được mở hay chưa, không xác nhận việc dùng thuốc. Vui lòng liên hệ người dùng hộp thuốc để kiểm tra.

            Email này được gửi cho chủ hộp và mọi người thân đang theo dõi hộp thuốc.
            """.ReplaceLineEndings("\n"), message.Body);
    }

    [Fact]
    public void DoseAlert_UsesScheduleTimeZoneAndSlotLabel()
    {
        var message = EmailTemplates.DoseAlert("a@example.com", AlertType.OverdueUnconfirmed, Medicines, ScheduledAt, "Europe/London", 2);

        Assert.Contains("lúc 00:00 ngày 12/11/2026 ở ngăn Trưa (ngăn 2)", message.Body);
    }

    [Theory]
    [InlineData(AlertType.OverdueUnconfirmed)]
    [InlineData(AlertType.UnconfirmedOffline)]
    public void DoseAlert_AnyTemplate_HasNoBannedWordingLinksOrOptOut(AlertType alertType)
    {
        var message = EmailTemplates.DoseAlert("a@example.com", alertType, Medicines, ScheduledAt, "Asia/Ho_Chi_Minh", 1);
        var text = message.Subject + "\n" + message.Body;

        foreach (var banned in new[] { "đã uống", "quên uống", "bỏ liều", "chưa uống", "http", "hủy đăng ký", "tắt email", "unsubscribe", "@" })
        {
            Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
