using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.Api.Security;

/// <summary>Phát/xóa cookie phiên (D-02) — một cách duy nhất cho API và Razor UI.</summary>
public static class SessionCookie
{
    public const string SessionIdClaim = "session_id";

    public static Task SignInAsync(HttpContext httpContext, LoginResult login)
    {
        var user = login.User!;
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(SessionIdClaim, login.SessionId!),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = login.ExpiresAt,
            });
    }

    public static Task SignOutAsync(HttpContext httpContext, IUserSessionStore sessions)
    {
        var sessionId = httpContext.User.FindFirstValue(SessionIdClaim);
        if (!string.IsNullOrEmpty(sessionId))
        {
            sessions.Revoke(sessionId);
        }

        return httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
