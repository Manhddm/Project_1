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
