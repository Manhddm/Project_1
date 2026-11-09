using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace SmartMedBox.Api.Errors;

public static class ApiProblems
{
    public const string ContentType = "application/problem+json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, string> DefaultDetails =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ErrorCodes.ValidationError] = "Dữ liệu không hợp lệ.",
            [ErrorCodes.Unauthorized] = "Cần đăng nhập để tiếp tục.",
            [ErrorCodes.InvalidCredentials] = "Email hoặc mật khẩu không đúng.",
            [ErrorCodes.EmailNotVerified] = "Email chưa được xác minh.",
            [ErrorCodes.CsrfInvalid] = "Thiếu hoặc sai CSRF token.",
            [ErrorCodes.DeviceUnauthorized] = "Không xác thực được thiết bị.",
            [ErrorCodes.Forbidden] = "Bạn không có quyền thực hiện thao tác này.",
            [ErrorCodes.NotFound] = "Không tìm thấy tài nguyên.",
            [ErrorCodes.MethodNotAllowed] = "Phương thức HTTP không được hỗ trợ.",
            [ErrorCodes.Conflict] = "Xung đột dữ liệu.",
            [ErrorCodes.UnsupportedMediaType] = "Content-Type không được hỗ trợ.",
            [ErrorCodes.RateLimited] = "Bạn đã gửi quá nhiều yêu cầu, vui lòng thử lại sau.",
            [ErrorCodes.InternalError] = "Đã xảy ra lỗi hệ thống.",
        };

    public static ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string code,
        string? detail = null,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Type = "about:blank",
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Status = statusCode,
            Detail = detail ?? GetDefaultDetail(code),
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = GetTraceId(httpContext);

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    public static string GetTraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    public static async Task WriteAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var problem = Create(httpContext, statusCode, code, detail);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = ContentType;
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions), cancellationToken);
    }

    private static string GetDefaultDetail(string code) =>
        DefaultDetails.TryGetValue(code, out var detail) ? detail : "Yêu cầu không thực hiện được.";
}
