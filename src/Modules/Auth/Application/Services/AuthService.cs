using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Nexum.Modules.Auth.Application.DTOs;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using OtpNet;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Nexum.Modules.Auth.Application.Services;

public interface IAuthService
{
    Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<ApiResponse<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task<ApiResponse<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken ct = default);
    Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task<ApiResponse<string>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default);
    Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<ApiResponse<UserDto>> GetCurrentUserAsync(string userId, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateFcmTokenAsync(string userId, UpdateFcmTokenRequest request, CancellationToken ct = default);
    Task<ApiResponse<UserDto>> CreateOfficerAsync(CreateOfficerRequest request, CancellationToken ct = default);
}

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly NexumDbContext _db;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        SignInManager<ApplicationUser> signInManager,
        NexumDbContext db,
        IConfiguration config,
        IEmailService emailService,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _db = db;
        _config = config;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ApiResponse<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
            return ApiResponse<AuthResponse>.Fail("EMAIL_ALREADY_REGISTERED",
                "This email address is already registered.");

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactPhone = request.EmergencyContactPhone,
            EstateOrZone = request.EstateOrZone,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(e => new { e.Code, e.Description });
            var errorDescriptions = string.Join("; ", result.Errors.Select(e => e.Description));
            return ApiResponse<AuthResponse>.Fail("VALIDATION_ERROR", $"Registration failed. {errorDescriptions}", errors);
        }

        await _userManager.AddToRoleAsync(user, Roles.Worshipper);
        _logger.LogInformation("User {UserId} registered as worshipper", user.Id);

        var response = await BuildAuthResponseAsync(user);
        return ApiResponse<AuthResponse>.Ok(response);
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return ApiResponse<AuthResponse>.Fail("INVALID_CREDENTIALS", "Email or password is incorrect.");

        if (user.AccountStatus == AccountStatus.Suspended)
            return ApiResponse<AuthResponse>.Fail("ACCOUNT_SUSPENDED", "Your account has been suspended.");

        if (user.LockoutEnabled && user.LockoutEnd > DateTimeOffset.UtcNow)
            return ApiResponse<AuthResponse>.Fail("ACCOUNT_LOCKED",
                "Account locked due to too many attempts. Try again later.");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
                return ApiResponse<AuthResponse>.Fail("ACCOUNT_LOCKED",
                    "Account locked due to too many attempts. Try again in 15 minutes.");
            return ApiResponse<AuthResponse>.Fail("INVALID_CREDENTIALS", "Email or password is incorrect.");
        }

        var response = await BuildAuthResponseAsync(user);
        return ApiResponse<AuthResponse>.Ok(response);
    }

    public async Task<ApiResponse<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = _db.RefreshTokens.FirstOrDefault(rt => rt.TokenHash == tokenHash);

        if (stored is null || !stored.IsActive)
            return ApiResponse<AuthResponse>.Fail("REFRESH_TOKEN_EXPIRED",
                "Refresh token is expired or revoked. Please login again.");

        var user = await _userManager.FindByIdAsync(stored.UserId);
        if (user is null)
            return ApiResponse<AuthResponse>.Fail("TOKEN_INVALID", "Invalid token.");

        // Rotate: revoke old, issue new
        stored.RevokedAt = DateTime.UtcNow;
        var response = await BuildAuthResponseAsync(user);
        await _db.SaveChangesAsync(ct);

        return ApiResponse<AuthResponse>.Ok(response);
    }

    public async Task<ApiResponse<bool>> LogoutAsync(string userId, string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = HashToken(refreshToken);
        var stored = _db.RefreshTokens.FirstOrDefault(rt =>
            rt.UserId == userId && rt.TokenHash == tokenHash);

        if (stored is not null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        // Always return 200 to prevent email enumeration
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return ApiResponse<bool>.Ok(true);

        // Generate TOTP-based OTP
        var key = KeyGeneration.GenerateRandomKey(20);
        var totp = new Totp(key, step: 600); // 10-min window
        var code = totp.ComputeTotp();
        var codeHash = HashToken(code);

        var otpEntry = new OtpCode
        {
            UserId = user.Id,
            CodeHash = codeHash,
            Purpose = "forgot_password",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        _db.OtpCodes.Add(otpEntry);
        await _db.SaveChangesAsync(ct);

        await _emailService.SendAsync(
            user.Email!, user.FullName,
            "Nexum — Password Reset Code",
            $"<p>Your password reset code is: <strong style='font-size:24px;letter-spacing:4px'>{code}</strong></p>" +
            $"<p>This code expires in 10 minutes.</p>",
            ct);

        _logger.LogInformation("OTP sent for password reset to user {UserId}", user.Id);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<string>> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return ApiResponse<string>.Fail("OTP_INVALID", "Invalid code.");

        var codeHash = HashToken(request.Otp);
        var otp = _db.OtpCodes
            .Where(o => o.UserId == user.Id && o.Purpose == "forgot_password" && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefault();

        if (otp is null || otp.CodeHash != codeHash)
            return ApiResponse<string>.Fail("OTP_INVALID", "Incorrect code.");
        if (otp.IsExpired)
            return ApiResponse<string>.Fail("OTP_EXPIRED", "Code has expired. Please request a new one.");
        if (otp.IsUsed)
            return ApiResponse<string>.Fail("OTP_ALREADY_USED", "This code has already been used.");

        otp.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Issue short-lived reset JWT (5 minutes)
        var resetToken = GenerateResetToken(user.Id);
        return ApiResponse<string>.Ok(resetToken);
    }

    public async Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        string userId;
        try
        {
            userId = ValidateResetToken(request.ResetToken);
        }
        catch
        {
            return ApiResponse<bool>.Fail("RESET_TOKEN_INVALID",
                "Reset token is invalid or expired. Please start again.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return ApiResponse<bool>.Fail("RESET_TOKEN_INVALID", "Invalid token.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);

        if (!result.Succeeded)
            return ApiResponse<bool>.Fail("VALIDATION_ERROR", "Password does not meet requirements.",
                result.Errors.Select(e => new { e.Code, e.Description }));

        _logger.LogInformation("Password reset for user {UserId}", user.Id);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<UserDto>> GetCurrentUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return ApiResponse<UserDto>.Fail("NOT_FOUND", "User not found.");

        var roles = await _userManager.GetRolesAsync(user);
        var dto = MapToDto(user, roles.FirstOrDefault() ?? Roles.Worshipper);
        return ApiResponse<UserDto>.Ok(dto);
    }

    public async Task<ApiResponse<bool>> UpdateProfileAsync(string userId, UpdateProfileRequest request,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "User not found.");

        if (request.FullName is not null) user.FullName = request.FullName;
        if (request.PhoneNumber is not null) user.PhoneNumber = request.PhoneNumber;
        if (request.EmergencyContactName is not null) user.EmergencyContactName = request.EmergencyContactName;
        if (request.EmergencyContactPhone is not null) user.EmergencyContactPhone = request.EmergencyContactPhone;
        if (request.EstateOrZone is not null) user.EstateOrZone = request.EstateOrZone;
        user.UpdatedAt = DateTime.UtcNow;

        await _userManager.UpdateAsync(user);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> UpdateFcmTokenAsync(string userId, UpdateFcmTokenRequest request,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "User not found.");

        user.FcmToken = request.FcmToken;
        user.ApnsToken = request.ApnsToken;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<UserDto>> CreateOfficerAsync(CreateOfficerRequest request,
        CancellationToken ct = default)
    {
        if (!Roles.All.Contains(request.Role) || request.Role == Roles.Worshipper)
            return ApiResponse<UserDto>.Fail("VALIDATION_ERROR",
                "Invalid role. Must be medical_officer, security_officer, driver, or admin.");

        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
            return ApiResponse<UserDto>.Fail("EMAIL_ALREADY_REGISTERED", "Email already registered.");

        var allowedRoles = new[]
        {
            Roles.MedicalOfficer, Roles.SecurityOfficer,
            Roles.Driver, Roles.Host, Roles.Admin
        };

        if (!allowedRoles.Contains(request.Role))
            return ApiResponse<UserDto>.Fail("INVALID_ROLE",
                $"Role must be one of: {string.Join(", ", allowedRoles)}");

        var tempPassword = GenerateTempPassword();
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, tempPassword);
        if (!result.Succeeded)
            return ApiResponse<UserDto>.Fail("VALIDATION_ERROR", "Failed to create user.",
                result.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, request.Role);

        await _emailService.SendAsync(user.Email!, user.FullName,
            "Welcome to Nexum",
            $"<p>Your account has been created. Temporary password: <strong>{tempPassword}</strong></p>" +
            "<p>Please login and change your password immediately.</p>");

        _logger.LogInformation("Officer {UserId} created with role {Role}", user.Id, request.Role);
        var dto = MapToDto(user, request.Role);
        return ApiResponse<UserDto>.Ok(dto);
    }

    // ── Private helpers ──

    private async Task<AuthResponse> BuildAuthResponseAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? Roles.Worshipper;
        var accessToken = GenerateAccessToken(user, role);
        var (refreshToken, refreshHash) = GenerateRefreshToken();

        // Store refresh token
        var existing = _db.RefreshTokens.Where(rt => rt.UserId == user.Id && rt.RevokedAt == null).ToList();
        foreach (var old in existing) old.RevokedAt = DateTime.UtcNow;

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await _db.SaveChangesAsync();

        return new AuthResponse(accessToken, refreshToken, 3600, MapToDto(user, role));
    }

    private string GenerateAccessToken(ApplicationUser user, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateResetToken(string userId)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:ResetSecret"] ?? _config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.NameIdentifier, userId),
                     new Claim("purpose", "password_reset")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string ValidateResetToken(string token)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:ResetSecret"] ?? _config["Jwt:Secret"]!));
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        }, out _);

        var purposeClaim = principal.FindFirst("purpose")?.Value;
        if (purposeClaim != "password_reset") throw new SecurityTokenException("Invalid token purpose.");

        return principal.FindFirst(ClaimTypes.NameIdentifier)!.Value;
    }

    private static (string Token, string Hash) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(bytes);
        return (token, HashToken(token));
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLower();
    }

    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$";
        var bytes = RandomNumberGenerator.GetBytes(12);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    private static UserDto MapToDto(ApplicationUser user, string role) =>
        new(user.Id, user.FullName, user.Email!, user.PhoneNumber, role,
            user.EmergencyContactName, user.EmergencyContactPhone, user.EstateOrZone);
}
