using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Properties (public browse + host management) ──────────────
[ApiController]
[Route("v1/properties")]
public sealed class PropertiesController : ControllerBase
{
    private readonly IPropertyService _service;
    public PropertiesController(IPropertyService service) => _service = service;

    // Public — no auth required
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{propertyId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid propertyId, CancellationToken ct)
    {
        var result = await _service.GetAsync(propertyId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpGet("{propertyId:guid}/room-types")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRoomTypes(Guid propertyId, CancellationToken ct)
    {
        var result = await _service.GetRoomTypesAsync(propertyId, ct);
        return Ok(result);
    }

    [HttpGet("{propertyId:guid}/availability")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailability(
        Guid propertyId,
        [FromQuery] DateOnly checkIn,
        [FromQuery] DateOnly checkOut,
        CancellationToken ct)
    {
        var result = await _service.GetAvailabilityAsync(propertyId, checkIn, checkOut, ct);
        return Ok(result);
    }

    // Host — authenticated
    [HttpPost]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> Create([FromBody] CreatePropertyRequest request, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateAsync(hostId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{propertyId:guid}")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> Update(Guid propertyId,
        [FromBody] UpdatePropertyRequest request, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateAsync(hostId, propertyId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{propertyId:guid}/room-types")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> AddRoomType(Guid propertyId,
        [FromBody] CreateRoomTypeRequest request, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.AddRoomTypeAsync(hostId, propertyId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{propertyId:guid}/room-types/{roomTypeId:guid}/availability")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    public async Task<IActionResult> SetAvailability(Guid propertyId, Guid roomTypeId,
        [FromBody] CreateRoomAvailabilityRequest request, CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.SetAvailabilityAsync(hostId, roomTypeId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}