namespace SmartMedBox.Api.Security;

/// <summary>Header bảo mật cho mọi response (HTML, API, file tĩnh, lỗi) — P7-03.</summary>
public static class SecurityHeaders
{
    // Razor + JS cùng origin, không có script/style inline; blob: cho xem ảnh đơn thuốc tải qua fetch.
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // OnStarting: ExceptionHandler xóa header khi dọn response lỗi, nên đặt ngay trước khi gửi.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
