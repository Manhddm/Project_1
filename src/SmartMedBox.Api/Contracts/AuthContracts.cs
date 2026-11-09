using System.ComponentModel.DataAnnotations;

namespace SmartMedBox.Api.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed record RegisterResponse(Guid UserId, string Email, bool EmailVerificationRequired);

public sealed class VerifyEmailRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

public sealed record VerifyEmailResponse(bool EmailVerified);

public sealed class ResendVerificationRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed record MessageResponse(string Message);

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Password { get; set; } = string.Empty;
}

public sealed record UserDto(Guid Id, string Email, bool EmailVerified);

public sealed record LoginResponse(UserDto User);

public sealed record OwnedDeviceDto(Guid Id, string SerialNumber, DateTimeOffset? PairedAt);

public sealed record MeResponse(
    Guid Id,
    string Email,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    OwnedDeviceDto? OwnedDevice);
