using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Admin bookings ────────────────────────────────────────────
[ApiController]
[Route("v1/host/bank-accounts")]
[Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
public sealed class HostBankAccountController : ControllerBase
{
    private readonly IHostBankAccountService _service;
    public HostBankAccountController(IHostBankAccountService service) => _service = service;

    /// <summary>
    /// Get list of all Nigerian banks from Paystack.
    /// Use this to populate the bank dropdown in the UI.
    /// </summary>
    [HttpGet("banks")]
    public async Task<IActionResult> GetBanks(CancellationToken ct)
    {
        var result = await _service.GetBanksAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Verify a bank account before saving.
    /// Returns the account holder's name as it appears at the bank.
    /// Call this when the host fills in their account number so they
    /// can confirm it's correct before saving.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyAccountRequest request, CancellationToken ct)
    {
        var result = await _service.VerifyAccountAsync(request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Save a verified bank account.
    /// This calls Paystack to verify the account AND create a transfer recipient.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Add(
        [FromBody] AddBankAccountRequest request, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email = User.FindFirstValue(ClaimTypes.Email)!;
        var result = await _service.AddBankAccountAsync(hostId, email, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>Get all saved bank accounts for the current host.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyBankAccountsAsync(hostId, ct);
        return Ok(result);
    }

    /// <summary>Remove a saved bank account.</summary>
    [HttpDelete("{accountId:guid}")]
    public async Task<IActionResult> Remove(Guid accountId, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.RemoveBankAccountAsync(hostId, accountId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}