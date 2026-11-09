using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Routing;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Email;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.Infrastructure.Security;
using SmartMedBox.Infrastructure.Storage;
using SystemClock = SmartMedBox.Infrastructure.Time.SystemClock;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Default is required. Configure it with user-secrets or environment variables (see README).");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

var tokenPepper = builder.Configuration["Security:TokenPepper"];
if (string.IsNullOrWhiteSpace(tokenPepper) || tokenPepper.Length < 32)
{
    throw new InvalidOperationException(
        "Security:TokenPepper is required and must have at least 32 characters. Configure it with user-secrets or environment variables (see README).");
}

builder.Services.Configure<TokenPepperOptions>(options => options.TokenPepper = tokenPepper);
builder.Services.AddControllersWithViews(options => options.Filters.Add<ApiAntiforgeryFilter>());
builder.Services.AddSingleton<ProblemDetailsFactory, ApiProblemDetailsFactory>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "SmartMedBox.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SmartMedBox.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = UserSessionDefaults.Lifetime;
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
            ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized);
        options.Events.OnRedirectToAccessDenied = context =>
            ApiProblems.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        options.Events.OnValidatePrincipal = async context =>
        {
            var sessionId = context.Principal?.FindFirstValue("session_id");
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (sessionId is null
                || !Guid.TryParse(userIdValue, out var userId)
                || !context.HttpContext.RequestServices.GetRequiredService<IUserSessionStore>().IsActive(sessionId, userId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddRouting(options =>
{
    // Route MVC mặc định không được nuốt path /api, nếu không routing trả 404 thay vì 405 cho sai method.
    options.ConstraintMap["notapi"] = typeof(NotApiControllerConstraint);
});
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEmailSender, FileEmailSender>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasherService>();
builder.Services.AddSingleton<IUserSessionStore, InMemoryUserSessionStore>();
builder.Services.AddSingleton<ITokenHasher, HmacTokenHasher>();
builder.Services.AddScoped<DeviceProvisioningService>();
builder.Services.AddScoped<DeviceAccessService>();
builder.Services.AddScoped<RegistrationService>();
builder.Services.AddScoped<PasswordResetService>();

builder.Services.Configure<EmailSinkOptions>(options =>
    options.DirectoryPath = ResolveContentPath(
        builder.Environment.ContentRootPath,
        builder.Configuration["EmailSink:DirectoryPath"],
        "App_Data/email-sink"));
builder.Services.Configure<FileStorageOptions>(options =>
    options.RootPath = ResolveContentPath(
        builder.Environment.ContentRootPath,
        builder.Configuration["FileStorage:RootPath"],
        "App_Data/uploads"));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ApiModelStateProblems.Create;
});

var app = builder.Build();

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseHttpLogging();
app.UseStatusCodePages(ApiStatusCodePages.HandleAsync);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "root",
    pattern: "",
    defaults: new { controller = "Home", action = "Index" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller:notapi}/{action=Index}/{id?}");

app.Run();

static string ResolveContentPath(string contentRootPath, string? configuredPath, string fallbackPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath) ? fallbackPath : configuredPath;
    return Path.GetFullPath(Path.Combine(contentRootPath, path));
}

public partial class Program;
