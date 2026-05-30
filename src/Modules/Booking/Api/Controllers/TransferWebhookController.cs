using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Admin bookings ────────────────────────────────────────────
[ApiController]
[Route("v1/payments")]
public sealed class TransferWebhookController : ControllerBase
{
    private readonly IPayoutService _payoutService;
    public TransferWebhookController(IPayoutService payoutService) => _payoutService = payoutService;

    /// <summary>
    /// Handles transfer.success, transfer.failed, transfer.reversed events from Paystack.
    /// Separate endpoint from the payment webhook so each concern is isolated.
    /// </summary>
    [HttpPost("webhook/paystack/transfer")]
    [AllowAnonymous]
    public async Task<IActionResult> TransferWebhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new System.IO.StreamReader(Request.Body,
            leaveOpen: true, encoding: System.Text.Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["x-paystack-signature"].ToString();
        await _payoutService.HandleTransferWebhookAsync(payload, signature, ct);

        return Ok(new { status = true });
    }
}
