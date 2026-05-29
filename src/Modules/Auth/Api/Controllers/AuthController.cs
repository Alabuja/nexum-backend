using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Application.DTOs;
using Nexum.Modules.Auth.Application.Services;
using Nexum.Modules.Auth.Domain.Enums;
using System.Security.Claims;

namespace Nexum.Modules.Auth.Api.Controllers;

[ApiController]
[Route("v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        if (!result.Success) return Unauthorized(result);

        // Set HttpOnly cookie for web portal
        if (Request.Headers["X-Client-Type"].ToString() == "web")
        {
            Response.Cookies.Append("nexum_session", result.Data!.AccessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddMinutes(30)
            });
        }

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _auth.RefreshAsync(request.RefreshToken, ct);
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.LogoutAsync(userId, request.RefreshToken, ct);
        Response.Cookies.Delete("nexum_session");
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.GetCurrentUserAsync(userId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("me/profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.UpdateProfileAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("me/fcm-token")]
    [Authorize]
    public async Task<IActionResult> UpdateFcmToken([FromBody] UpdateFcmTokenRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _auth.UpdateFcmTokenAsync(userId, request, ct);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await _auth.ForgotPasswordAsync(request, ct);
        return Ok(result); // Always 200
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request, CancellationToken ct)
    {
        var result = await _auth.VerifyOtpAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await _auth.ResetPasswordAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

[ApiController]
[Route("v1/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController : ControllerBase
{
    private readonly IAuthService _auth;
    public AdminController(IAuthService auth) => _auth = auth;

    [HttpPost("users")]
    public async Task<IActionResult> CreateOfficer([FromBody] CreateOfficerRequest request, CancellationToken ct)
    {
        var result = await _auth.CreateOfficerAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
