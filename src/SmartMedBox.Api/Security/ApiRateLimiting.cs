using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SmartMedBox.Api.Errors;

namespace SmartMedBox.Api.Security;

/// <summary>Tên policy dùng với <c>[EnableRateLimiting]</c> (API_CONTRACT §1.6, D-14).</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Pair = "pair";
    public const string InvitationCreate = "invitation-create";
    public const string InvitationAccept = "invitation-accept";
}

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

/// <summary>Ngưỡng đọc từ section <c>RateLimiting</c>; mặc định theo D-14.</summary>
public sealed class RateLimitingOptions
{
    public RateLimitRule Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 15 * 60 };
    public RateLimitRule Pair { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public RateLimitRule InvitationCreate { get; set; } = new() { PermitLimit = 10, WindowSeconds = 24 * 60 * 60 };
    public RateLimitRule InvitationAccept { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    internal IEnumerable<RateLimitRule> All => [Login, Pair, InvitationCreate, InvitationAccept];
}

public static class ApiRateLimiting
{
    private const int SegmentsPerWindow = 6;

    // shortcut: bộ đếm nằm trong bộ nhớ từng process (như session D-27); nhiều instance thì mỗi instance đếm riêng — chuyển sang store dùng chung khi scale-out.
    // shortcut: khóa IP lấy từ kết nối trực tiếp; chạy sau reverse proxy phải bật ForwardedHeaders, nếu không mọi người dùng chung một IP.
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection("RateLimiting"))
            .Validate(o => o.All.All(r => r.PermitLimit > 0 && r.WindowSeconds > 0), "RateLimiting: PermitLimit và WindowSeconds phải > 0.")
            .ValidateOnStart();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                var policy = httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                if (policy is not null)
                {
                    // SlidingWindowRateLimiter không trả metadata RetryAfter; dùng cả cửa sổ để không bao giờ báo thử lại quá sớm.
                    var rule = SelectRule(httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value, policy);
                    httpContext.Response.Headers.RetryAfter = rule.WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                // Form Razor (UI-01) không đọc được ProblemDetails JSON; trả văn bản thuần cùng status + Retry-After.
                if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await httpContext.Response.WriteAsync(
                        "Bạn đã thử quá nhiều lần, vui lòng thử lại sau.",
                        cancellationToken);
                    return;
                }

                await ApiProblems.WriteAsync(
                    httpContext,
                    StatusCodes.Status429TooManyRequests,
                    ErrorCodes.RateLimited,
                    cancellationToken: cancellationToken);
            };

            AddPolicy(options, RateLimitPolicies.Login, ByIp);
            AddPolicy(options, RateLimitPolicies.Pair, ByIp);
            AddPolicy(options, RateLimitPolicies.InvitationCreate, ByUserOrIp);
            AddPolicy(options, RateLimitPolicies.InvitationAccept, ByIp);
        });
    }

    private static void AddPolicy(
        RateLimiterOptions options,
        string name,
        Func<HttpContext, string> partitionKey) =>
        options.AddPolicy(name, httpContext =>
        {
            var rule = SelectRule(httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value, name);
            return RateLimitPartition.GetSlidingWindowLimiter(partitionKey(httpContext), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
            });
        });

    private static RateLimitRule SelectRule(RateLimitingOptions options, string policy) => policy switch
    {
        RateLimitPolicies.Login => options.Login,
        RateLimitPolicies.Pair => options.Pair,
        RateLimitPolicies.InvitationCreate => options.InvitationCreate,
        RateLimitPolicies.InvitationAccept => options.InvitationAccept,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown rate limit policy."),
    };

    private static string ByIp(HttpContext httpContext) =>
        "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static string ByUserOrIp(HttpContext httpContext) =>
        httpContext.User.TryGetUserId(out var userId) ? "user:" + userId : ByIp(httpContext);
}
