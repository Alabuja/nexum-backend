using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexum.Modules.Auth.Application.DTOs;
using Nexum.Modules.Auth.Application.Services;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Auth.Api.Controllers;

[ApiController]
[Route("v1/admin/users")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthService _auth;

    public AdminUsersController(UserManager<ApplicationUser> userManager, IAuthService auth)
    {
        _userManager = userManager;
        _auth = auth;
    }

    // ── List all users (all roles) ────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        // Build query — EF Core + Identity
        var query = _userManager.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(s) ||
                u.Email!.ToLower().Contains(s) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(s)));
        }

        var allUsers = await query.OrderBy(u => u.FullName).ToListAsync(ct);

        // Filter by role (requires iterating — IdentityUserRole is not directly queryable here)
        var result = new List<UserDto>();
        foreach (var user in allUsers)
        {
            var roles = await _userManager.GetRolesAsync(user);

            if (!string.IsNullOrWhiteSpace(role) &&
                !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                continue;

            result.Add(new UserDto(
                user.Id,
                user.FullName,
                user.Email!,
                user.PhoneNumber,
                roles.FirstOrDefault() ?? "",
                user.EmergencyContactName,
                user.EmergencyContactPhone,
                user.EstateOrZone,
                user.EmailConfirmed));
        }

        var total = result.Count;
        var paged = result.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
        {
            Items = paged,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        }));
    }

    // ── Get single user ───────────────────────────────────────
    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return NotFound(ApiResponse<UserDto>.Fail("USER_NOT_FOUND", "User not found."));

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(ApiResponse<UserDto>.Ok(new UserDto(
            user.Id, user.FullName, user.Email!, user.PhoneNumber,
            roles.FirstOrDefault() ?? "", user.EmergencyContactName,
            user.EmergencyContactPhone, user.EstateOrZone, user.EmailConfirmed)));
    }

    // ── Create officer / driver account ──────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOfficerRequest request, CancellationToken ct)
    {
        var result = await _auth.CreateOfficerAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    // ── Update user role ──────────────────────────────────────
    [HttpPut("{userId}/role")]
    public async Task<IActionResult> UpdateRole(
        string userId, [FromBody] UpdateRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return NotFound(ApiResponse<bool>.Fail("USER_NOT_FOUND", "User not found."));

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, request.Role);

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Deactivate user ───────────────────────────────────────
    [HttpDelete("{userId}")]
    public async Task<IActionResult> Deactivate(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return NotFound(ApiResponse<bool>.Fail("USER_NOT_FOUND", "User not found."));

        // Lock account instead of deleting — preserves audit trail
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.SetLockoutEnabledAsync(user, true);

        return Ok(ApiResponse<bool>.Ok(true));
    }
}

// ── DTOs ──────────────────────────────────────────────────────
public sealed record UserDto(
    string Id,
    string FullName,
    string Email,
    string? PhoneNumber,
    string Role,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? EstateOrZone,
    bool EmailConfirmed
);

public sealed record UpdateRoleRequest([Required] string Role);
