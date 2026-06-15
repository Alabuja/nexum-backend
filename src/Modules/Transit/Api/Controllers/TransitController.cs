using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Transit.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Transit.Api.Controllers;

[ApiController]
[Route("v1/transit")]
public sealed class TransitController : ControllerBase
{
    private readonly ITransitService _service;
    public TransitController(ITransitService service) => _service = service;

    [HttpGet("network/nodes")]
    public async Task<IActionResult> GetNodes([FromQuery] string? nodeType, CancellationToken ct)
    {
        var result = await _service.GetNodesAsync(nodeType, ct);
        return Ok(result);
    }

    [HttpGet("network/edges")]
    public async Task<IActionResult> GetEdges(CancellationToken ct)
    {
        var result = await _service.GetEdgesAsync(ct);
        return Ok(result);
    }

    [HttpPost("shuttle-requests")]
    [Authorize]
    public async Task<IActionResult> CreateRequest([FromBody] CreateShuttleRequestDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateRequestAsync(userId, dto, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("shuttle-requests/mine")]
    [Authorize]
    public async Task<IActionResult> GetMyRequest(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyRequestAsync(userId, ct);
        return Ok(result);
    }

    [HttpPut("shuttle-requests/{requestId:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelRequest(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CancelRequestAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("vehicles/live")]
    [Authorize(Roles = "driver,admin")]
    public async Task<IActionResult> GetLiveVehicles(CancellationToken ct)
    {
        var result = await _service.GetLiveVehiclesAsync(ct);
        return Ok(result);
    }

    [HttpPut("drivers/availability")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> UpdateAvailability([FromBody] UpdateDriverAvailabilityDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateDriverAvailabilityAsync(userId, dto, ct);
        return Ok(result);
    }

    [HttpPut("drivers/location")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateDriverLocationDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateDriverLocationAsync(userId, dto, ct);
        return Ok(result);
    }

    [HttpPut("drivers/requests/{requestId:guid}/pickup")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> Pickup(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.PickupPassengerAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("drivers/requests/{requestId:guid}/complete")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> Complete(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CompleteRideAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("network/edges/{edgeId:guid}/close")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CloseEdge(Guid edgeId, CancellationToken ct)
    {
        var result = await _service.CloseEdgeAsync(edgeId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("network/edges/{edgeId:guid}/open")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> OpenEdge(Guid edgeId, CancellationToken ct)
    {
        var result = await _service.OpenEdgeAsync(edgeId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
