using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Security;
using SmartMedBox.Domain.Accounts;
using SmartMedBox.Infrastructure.Identity;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.Api.Controllers;

/// <summary>Biểu mẫu chung cho các trang tài khoản; mỗi trang chỉ dùng trường nó cần.</summary>
public sealed class AccountForm
{
    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? Token { get; set; }
}

/// <summary>Trang tài khoản Razor (UI-01). Mọi nghiệp vụ nằm trong service dùng chung với <see cref="AuthController"/>.</summary>
[Route("account")]
public sealed class AccountController(
    IUserSessionStore sessions,
    LoginService login,
    RegistrationService registration,
    PasswordResetService passwordReset) : Controller
{
    private const string InvalidEmailMessage = "Email không hợp lệ.";
    private const string TokenInvalidMessage = "Mã không đúng hoặc đã được sử dụng.";
    private const string TokenExpiredMessage = "Mã đã hết hạn. Vui lòng yêu cầu mã mới.";

    [HttpGet("")]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return RedirectToAction(nameof(Login));
        }

        ViewData["Email"] = User.FindFirstValue(ClaimTypes.Email);
        return View();
    }

    [HttpGet("register")]
    public IActionResult Register() => View(new AccountForm());

    [HttpPost("register")]
    public async Task<IActionResult> Register(AccountForm form, CancellationToken cancellationToken)
    {
        if (!TryNormalizeEmail(form.Email, out var email))
        {
            ModelState.AddModelError(nameof(AccountForm.Email), InvalidEmailMessage);
        }

        if (!PasswordPolicy.IsValid(form.Password))
        {
            ModelState.AddModelError(nameof(AccountForm.Password), AuthController.PasswordPolicyMessage);
        }

        if (ModelState.ErrorCount > 0)
        {
            return View(form);
        }

        var result = await registration.RegisterAsync(email, form.Password!, cancellationToken);
        if (result.Outcome == RegistrationOutcome.EmailTaken)
        {
            ModelState.AddModelError(nameof(AccountForm.Email), "Email này đã được đăng ký.");
            return View(form);
        }

        TempData["Message"] = "Đăng ký thành công. Hãy nhập mã xác minh đã gửi tới email của bạn.";
        return RedirectToAction(nameof(VerifyEmail));
    }

    // Email hiện chỉ chứa mã; ?token= cho phép điền sẵn nếu sau này email có liên kết.
    [HttpGet("verify-email")]
    public IActionResult VerifyEmail(string? token) => View(new AccountForm { Token = token });

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(AccountForm form, CancellationToken cancellationToken)
    {
        var outcome = await registration.VerifyEmailAsync(form.Token ?? string.Empty, cancellationToken);
        switch (outcome)
        {
            case EmailVerificationOutcome.Verified:
                TempData["Message"] = "Email đã được xác minh. Bạn có thể đăng nhập.";
                return RedirectToAction(nameof(Login));
            case EmailVerificationOutcome.Expired:
                ModelState.AddModelError(nameof(AccountForm.Token), TokenExpiredMessage);
                break;
            default:
                ModelState.AddModelError(nameof(AccountForm.Token), TokenInvalidMessage);
                break;
        }

        return View(form);
    }

    [HttpGet("resend-verification")]
    public IActionResult ResendVerification() => View(new AccountForm());

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(AccountForm form, CancellationToken cancellationToken)
    {
        await registration.ResendVerificationAsync(form.Email ?? string.Empty, cancellationToken);
        ModelState.Clear(); // Trang kết quả giống hệt nhau bất kể email nhập vào.
        ViewData["Message"] = AuthController.ResendVerificationMessage;
        return View(new AccountForm());
    }

    [HttpGet("login")]
    public IActionResult Login() => View(new AccountForm());

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task<IActionResult> Login(AccountForm form, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(form.Email ?? string.Empty, form.Password ?? string.Empty, cancellationToken);
        switch (result.Outcome)
        {
            case LoginOutcome.Succeeded:
                await SessionCookie.SignInAsync(HttpContext, result);
                return RedirectToAction(nameof(DashboardPagesController.Index), "DashboardPages");
            case LoginOutcome.InvalidEmail:
                ModelState.AddModelError(nameof(AccountForm.Email), InvalidEmailMessage);
                break;
            case LoginOutcome.EmailNotVerified:
                ViewData["NotVerified"] = true;
                ModelState.AddModelError(string.Empty, "Email chưa được xác minh.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
                break;
        }

        return View(new AccountForm { Email = form.Email });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await SessionCookie.SignOutAsync(HttpContext, sessions);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => View(new AccountForm());

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(AccountForm form, CancellationToken cancellationToken)
    {
        await passwordReset.ForgotPasswordAsync(form.Email ?? string.Empty, cancellationToken);
        ModelState.Clear(); // Trang kết quả giống hệt nhau bất kể email nhập vào.
        ViewData["Message"] = AuthController.ForgotPasswordMessage;
        return View(new AccountForm());
    }

    // Email hiện chỉ chứa mã; ?token= cho phép điền sẵn nếu sau này email có liên kết.
    [HttpGet("reset-password")]
    public IActionResult ResetPassword(string? token) => View(new AccountForm { Token = token });

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(AccountForm form, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.IsValid(form.Password))
        {
            ModelState.AddModelError(nameof(AccountForm.Password), AuthController.PasswordPolicyMessage);
            return View(new AccountForm { Token = form.Token });
        }

        var outcome = await passwordReset.ResetPasswordAsync(form.Token ?? string.Empty, form.Password!, cancellationToken);
        switch (outcome)
        {
            case PasswordResetOutcome.Reset:
                TempData["Message"] = "Mật khẩu đã được đặt lại. Vui lòng đăng nhập bằng mật khẩu mới.";
                return RedirectToAction(nameof(Login));
            case PasswordResetOutcome.Expired:
                ModelState.AddModelError(nameof(AccountForm.Token), TokenExpiredMessage);
                break;
            default:
                ModelState.AddModelError(nameof(AccountForm.Token), TokenInvalidMessage);
                break;
        }

        return View(new AccountForm { Token = form.Token });
    }

    private static bool TryNormalizeEmail(string? value, out string email)
    {
        try
        {
            email = EmailNormalizer.Normalize(value!);
            return true;
        }
        catch (ArgumentException)
        {
            email = string.Empty;
            return false;
        }
    }
}
