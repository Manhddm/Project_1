using Microsoft.AspNetCore.Diagnostics;

namespace SmartMedBox.Api.Errors;

public static class ApiStatusCodePages
{
    public static async Task HandleAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;
        if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var statusCode = httpContext.Response.StatusCode;
        var code = ErrorCodeMap.ForStatus(statusCode);
        if (code is null)
        {
            return;
        }

        await ApiProblems.WriteAsync(httpContext, statusCode, code, cancellationToken: httpContext.RequestAborted);
    }
}
