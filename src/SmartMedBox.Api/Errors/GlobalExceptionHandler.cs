using Microsoft.AspNetCore.Diagnostics;

namespace SmartMedBox.Api.Errors;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception. traceId={TraceId} method={Method} path={Path}",
            ApiProblems.GetTraceId(httpContext),
            httpContext.Request.Method,
            httpContext.Request.Path.Value);

        await ApiProblems.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            ErrorCodes.InternalError,
            cancellationToken: cancellationToken);

        return true;
    }
}
