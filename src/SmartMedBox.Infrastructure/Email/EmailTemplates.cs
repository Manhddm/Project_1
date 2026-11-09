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
}