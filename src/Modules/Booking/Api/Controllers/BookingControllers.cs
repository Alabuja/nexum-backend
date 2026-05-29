using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;
// ── Bookings ──────────────────────────────────────────────────
[ApiController]
[Route("v1/bookings")]
[Authorize]
public sealed class BookingsController : ControllerBase
{
    private readonly IBookingService _service;
    public BookingsController(IBookingService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var email  = User.FindFirstValue(ClaimTypes.Email)!;
        var result = await _service.CreateAsync(userId, email, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("mine")]
    public async Task<IActionResult> MyBookings(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyBookingsAsync(userId, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{bookingId:guid}")]
    public async Task<IActionResult> Get(Guid bookingId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetAsync(userId, bookingId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("lookup/{confirmationCode}")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> Lookup(string confirmationCode, CancellationToken ct)
    {
        var result = await _service.LookupByCodeAsync(confirmationCode, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPost("{bookingId:guid}/check-in")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> CheckIn(Guid bookingId, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CheckInAsync(hostId, bookingId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{bookingId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid bookingId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CancelAsync(userId, bookingId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
