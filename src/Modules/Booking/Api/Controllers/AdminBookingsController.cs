using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Admin bookings ────────────────────────────────────────────
[ApiController]
[Route("v1/admin/bookings")]
[Authorize(Roles = $"{Roles.Admin},{Roles.Host}")]
public sealed class AdminBookingsController : ControllerBase
{
    private readonly IBookingService _service;
    public AdminBookingsController(IBookingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.AdminListAsync(page, pageSize, ct);
        return Ok(result);
    }
}