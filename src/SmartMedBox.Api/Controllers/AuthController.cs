using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Contracts;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserSessionStore sessions,
    LoginService login,
    RegistrationService registration,
    PasswordResetService passwordReset) : ControllerBase
{
    internal const string PasswordPolicyMessage =
        "Mật khẩu phải có ít nhất 8 ký tự, tối đa 128 ký tự và gồm cả chữ lẫn số.";

    internal const string ResendVerificationMessage =
        "Nếu email tồn tại và chưa xác minh, chúng tôi đã gửi lại.";

    internal const string ForgotPasswordMessage =
        "Nếu email tồn tại, hướng dẫn đặt lại đã được gửi.";

    [HttpPost("register")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        string email;
        try
        {
            email = EmailNormalizer.Normalize(request.Email);
        }
        catch (ArgumentException)
        {
            return ApiProblemResults.Validation(HttpContext, "email", "Email không hợp lệ.");
        }

        if (!PasswordPolicy.IsValid(request.Password))
        {
            return ApiProblemResults.Validation(HttpContext, "password", PasswordPolicyMessage);
        }

        var result = await registration.RegisterAsync(email, request.Password, cancellationToken);
        if (result.Outcome == RegistrationOutcome.EmailTaken)
        {
            return ApiProblemResults.Create(
                HttpContext,
                StatusCodes.Status409Conflict,
                ErrorCodes.EmailTaken);
        }

        var user = result.User!;
        return StatusCode(
            StatusCodes.Status201Created,
            new RegisterResponse(user.Id, user.Email, EmailVerificationRequired: true));
    }

    [HttpPost("verify-email")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await registration.VerifyEmailAsync(request.Token, cancellationToken);
        return outcome switch
        {
            EmailVerificationOutcome.Verified => Ok(new VerifyEmailResponse(EmailVerified: true)),
            EmailVerificationOutcome.Expired => ApiProblemResults.Create(
                HttpContext,
                StatusCodes.Status410Gone,
                ErrorCodes.TokenExpired),
            _ => ApiProblemResults.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ErrorCodes.TokenInvalid),
        };
    }

    [HttpPost("resend-verification")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        await registration.ResendVerificationAsync(request.Email, cancellationToken);
        return Accepted(new MessageResponse(ResendVerificationMessage));
    }

    [HttpPost("forgot-password")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await passwordReset.ForgotPasswordAsync(request.Email, cancellationToken);
        return Accepted(new MessageResponse(ForgotPasswordMessage));
    }

    [HttpPost("reset-password")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return ApiProblemResults.Validation(HttpContext, "password", PasswordPolicyMessage);
        }

        var outcome = await passwordReset.ResetPasswordAsync(
            request.Token,
            request.NewPassword,
            cancellationToken);

        return outcome switch
        {
            PasswordResetOutcome.Reset => Ok(),
            PasswordResetOutcome.Expired => ApiProblemResults.Create(
                HttpContext,
                StatusCodes.Status410Gone,
                ErrorCodes.TokenExpired),
            _ => ApiProblemResults.Create(
                HttpContext,
                StatusCodes.Status400BadRequest,
                ErrorCodes.TokenInvalid),
        };
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(request.Email, request.Password, cancellationToken);
        switch (result.Outcome)
        {
            case LoginOutcome.InvalidEmail:
                return ApiProblemResults.Validation(HttpContext, "email", "Email không hợp lệ.");
            case LoginOutcome.InvalidCredentials:
                return ApiProblemResults.Create(
                    HttpContext,
                    StatusCodes.Status401Unauthorized,
                    ErrorCodes.InvalidCredentials);
            case LoginOutcome.EmailNotVerified:
                return ApiProblemResults.Create(
                    HttpContext,
                    StatusCodes.Status403Forbidden,
                    ErrorCodes.EmailNotVerified);
        }

        await SessionCookie.SignInAsync(HttpContext, result);
        var user = result.User!;
        return Ok(new LoginResponse(new UserDto(user.Id, user.Email, user.EmailVerified)));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await SessionCookie.SignOutAsync(HttpContext, sessions);
        return NoContent();
    }
}
