namespace SmartMedBox.Domain.Accounts;

public static class AuthTokenLifetime
{
    // D-14 (chốt 2026-10-29): token xác minh email và đặt lại mật khẩu sống 24 giờ.
    public static readonly TimeSpan EmailVerification = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordReset = TimeSpan.FromHours(24);

    // D-14: lời mời người thân hết hạn sau 7 ngày kể từ lúc tạo.
    public static readonly TimeSpan RelativeInvitation = TimeSpan.FromDays(7);
}