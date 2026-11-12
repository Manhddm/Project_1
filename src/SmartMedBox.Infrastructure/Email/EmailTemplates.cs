using System.Globalization;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.Infrastructure.Email;

public static class EmailTemplates
{
    public static EmailMessage EmailVerification(string to, string token, TimeSpan lifetime) =>
        new(
            to,
            "Xác minh email cho Hộp thuốc thông minh",
            $"""
            Xin chào,

            Mã xác minh email của bạn là:

            {token}

            Mã có hiệu lực {lifetime.TotalHours:0} giờ và chỉ dùng được một lần.

            Nếu bạn không tạo tài khoản, hãy bỏ qua email này.
            """);

    public static EmailMessage PasswordReset(string to, string token, TimeSpan lifetime) =>
        new(
            to,
            "Đặt lại mật khẩu Hộp thuốc thông minh",
            $"""
            Xin chào,

            Mã đặt lại mật khẩu của bạn là:

            {token}

            Mã có hiệu lực {lifetime.TotalHours:0} giờ và chỉ dùng được một lần. Sau khi đặt lại, mọi phiên đăng nhập cũ sẽ bị thu hồi.

            Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.
            """);

    private static readonly string[] SlotLabels = ["Sáng", "Trưa", "Tối"];

    // P5-04: recipient-neutral (same text for owner and relatives); wording only "đã mở/đã xác nhận" (AGENTS §2.2); no opt-out by design (§4.4).
    public static EmailMessage DoseAlert(
        string to, AlertType alertType, IEnumerable<string> medicineNames, DateTimeOffset scheduledAt, string timeZoneId, int slotNumber)
    {
        var local = TimeZoneInfo.ConvertTime(scheduledAt, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        var dose = $"Lần nhắc thuốc lúc {local.ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture)} ở ngăn {SlotLabels[slotNumber - 1]} (ngăn {slotNumber})";
        var finding = alertType switch
        {
            AlertType.OverdueUnconfirmed =>
                $"{dose} đã quá hạn và chưa được xác nhận: nắp ngăn này chưa được mở trong thời gian chờ, trong khi hộp thuốc vẫn đang kết nối.",
            AlertType.UnconfirmedOffline =>
                $"{dose}: không thể xác nhận do mất kết nối. Hộp thuốc không gửi được dữ liệu lên máy chủ khi hết thời gian chờ, nên hệ thống không biết nắp ngăn đã được mở hay chưa.",
            _ => throw new ArgumentOutOfRangeException(nameof(alertType)),
        };
        var medicines = string.Join('\n', medicineNames.Select(x => $"- {x}"));
        return new(
            to,
            "Cảnh báo lần uống từ Hộp thuốc thông minh",
            $"""
            Xin chào,

            {finding}

            Thuốc trong lần nhắc này:
            {medicines}

            Hệ thống chỉ biết nắp ngăn đã được mở hay chưa, không xác nhận việc dùng thuốc. Vui lòng liên hệ người dùng hộp thuốc để kiểm tra.

            Email này được gửi cho chủ hộp và mọi người thân đang theo dõi hộp thuốc.
            """.ReplaceLineEndings("\n")); // same bytes regardless of the source file's checkout line endings
    }

    public static EmailMessage RelativeInvitation(string to, string token, TimeSpan lifetime) =>
        new(
            to,
            "Lời mời theo dõi Hộp thuốc thông minh",
            $"""
            Xin chào,

            Bạn được chủ một Hộp thuốc thông minh mời làm người thân theo dõi. Người thân được xem lịch, trạng thái các ngăn (đã mở/đã xác nhận) và nhận email cảnh báo khi một lần uống quá hạn chưa được xác nhận. Hệ thống chỉ biết nắp ngăn đã được mở, không xác nhận việc uống thuốc.

            Mã lời mời của bạn là:

            {token}

            Đăng nhập bằng đúng email này rồi nhập mã để chấp nhận. Nếu chưa có tài khoản, hãy đăng ký và xác minh bằng chính email này trước.

            Mã có hiệu lực {lifetime.TotalDays:0} ngày và chỉ dùng được một lần.

            Nếu bạn không quen người mời, hãy bỏ qua email này.
            """);
}
