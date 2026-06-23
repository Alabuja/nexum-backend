using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Sms.Application;
using System.Security.Claims;

namespace Nexum.Modules.Sms.Api.Controllers;

[ApiController]
[Route("v1/sms")]
[Authorize]
public sealed class SmsController : ControllerBase
{
    private readonly ISmsService _service;

    public SmsController(ISmsService service) => _service = service;

    [HttpGet("wallet")]
    public async Task<IActionResult> Wallet(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetWalletAsync(userId, ct);
        return Ok(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetTransactionsAsync(userId, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("send-nexum-paid")]
    public async Task<IActionResult> SendNexumPaid(
        [FromBody] SendSmsRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.SendNexumPaidAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("send-with-tokens")]
    public async Task<IActionResult> SendWithTokens(
        [FromBody] SendSmsRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.SendWithTokensAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("phone-fallback-log")]
    public async Task<IActionResult> LogPhoneFallback(
        [FromBody] PhoneFallbackLogRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.LogPhoneFallbackAsync(userId, request, ct);
        return Ok(result);
    }

    [HttpPost("wallet/topup")]
    public async Task<IActionResult> InitiateTopup(
        [FromBody] InitiateSmsTopupRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.InitiateTopupAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("wallet/topup/verify")]
    public async Task<IActionResult> VerifyTopup(
        [FromBody] VerifySmsTopupRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.VerifyTopupAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("wallets/{userId}/credit")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> CreditUserWallet(
        string userId, [FromBody] CreditWalletRequest request, CancellationToken ct)
    {
        var result = await _service.CreditWalletAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("admin/wallets")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminWallets(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _service.AdminWalletsAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("admin/messages")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminMessages(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _service.AdminMessagesAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("admin/transactions")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdminTransactions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _service.AdminTransactionsAsync(page, pageSize, ct);
        return Ok(result);
    }
}
