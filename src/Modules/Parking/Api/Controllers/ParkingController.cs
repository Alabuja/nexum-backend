using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Emergency.Application;
using Nexum.Modules.Parking.Application;
using Nexum.SharedKernel.Interfaces;
using System.Security.Claims;

namespace Nexum.Modules.Parking.Api.Controllers;

[ApiController]
[Route("v1/parking")]
[Authorize]
public sealed class ParkingController : ControllerBase
{
    private readonly IParkingService _service;
    public ParkingController(IParkingService service) => _service = service;

    [HttpPost("pins")]
    public async Task<IActionResult> CreatePin([FromBody] CreatePinRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreatePinAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("pins/mine")]
    public async Task<IActionResult> GetMyPin(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyPinAsync(userId, ct);
        return Ok(result);
    }

    [HttpDelete("pins/{pinId:guid}")]
    public async Task<IActionResult> DeactivatePin(Guid pinId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.DeactivatePinAsync(userId, pinId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("blocking-alerts")]
    public async Task<IActionResult> CreateBlockingAlert(
        [FromBody] CreateBlockingAlertRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateBlockingAlertAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
