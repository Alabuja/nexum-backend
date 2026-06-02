using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Auth.Application.DTOs;

public sealed record RegisterRequest(
    [Required] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    string? PhoneNumber,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EstateOrZone
);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public sealed record ForgotPasswordRequest([Required, EmailAddress] string Email);

public sealed record VerifyOtpRequest(
    [Required, EmailAddress] string Email,
    [Required, Length(6, 6)] string Otp
);

public sealed record ResetPasswordRequest(
    [Required] string ResetToken,
    [Required, MinLength(8)] string NewPassword
);

public sealed record UpdateProfileRequest(
    string? FullName,
    string? PhoneNumber,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EstateOrZone
);

public sealed record UpdateFcmTokenRequest([Required] string FcmToken, string? ApnsToken);

public sealed record CreateOfficerRequest(
    [Required] string FullName,
    [Required, EmailAddress] string Email,
    [Required] string Role,
    string? PhoneNumber
);
 
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    UserDto User
);

public sealed record UserDto(
    string Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EstateOrZone
);

public sealed record RefreshTokenRequest([Required] string RefreshToken);
