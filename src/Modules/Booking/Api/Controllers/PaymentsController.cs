using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Paystack webhook ──────────────────────────────────────────
[ApiController]
[Route("v1/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IBookingService _service;
    public PaymentsController(IBookingService service) => _service = service;

    /// <summary>
    /// Paystack sends this webhook on successful payment.
    /// Verified via HMAC-SHA512 of the raw payload.
    /// Must return 200 quickly — Paystack retries on non-200.
    /// </summary>
    [HttpPost("webhook/paystack")]
    [AllowAnonymous]
    public async Task<IActionResult> PaystackWebhook(CancellationToken ct)
    {
        // Read raw body for signature verification
        Request.EnableBuffering();
        using var reader = new System.IO.StreamReader(Request.Body,
            leaveOpen: true, encoding: System.Text.Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["x-paystack-signature"].ToString();

        var result = await _service.HandlePaystackWebhookAsync(payload, signature, ct);

        // Always return 200 to Paystack — internal failures are logged
        return Ok(new { status = true });
    }
}
