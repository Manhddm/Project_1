using SmartMedBox.Api.Errors;

namespace SmartMedBox.Api.Observability;

public static class TraceIdLogScope
{
    public const string Key = "TraceId";

    // Mọi log trong request mang cùng traceId mà ProblemDetails trả cho client.
    public static IApplicationBuilder UseTraceIdLogScope(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("SmartMedBox.Request");
        return app.Use(async (context, next) =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { [Key] = ApiProblems.GetTraceId(context) }))
            {
                await next(context);
            }
        });
    }
}
