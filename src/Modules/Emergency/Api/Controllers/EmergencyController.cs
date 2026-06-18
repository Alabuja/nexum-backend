using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Emergency.Application;
using Nexum.SharedKernel.Interfaces;
using System.Security.Claims;

namespace Nexum.Modules.Emergency.Api.Controllers;

[ApiController]
[Route("v1/emergency")]
[Authorize]
public sealed class EmergencyController : ControllerBase
{
    private readonly IEmergencyService _service;
    public EmergencyController(IEmergencyService service) => _service = service;

    [HttpPost("report")]
    public async Task<IActionResult> Report([FromBody] ReportRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateReportAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("incidents")]
    [Authorize(Roles = "medical_officer,security_officer,admin")]
    public async Task<IActionResult> GetIncidents([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var officerRole = User.FindFirstValue(ClaimTypes.Role)!;
        var result = await _service.GetIncidentsAsync(officerId, officerRole, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("incidents/{incidentId:guid}")]
    [Authorize(Roles = "medical_officer,security_officer,admin")]
    public async Task<IActionResult> GetIncident(Guid incidentId, CancellationToken ct)
    {
        var result = await _service.GetIncidentAsync(incidentId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("incidents/{incidentId:guid}/status")]
    [Authorize(Roles = "medical_officer,security_officer,admin")]
    public async Task<IActionResult> UpdateStatus(Guid incidentId,
        [FromBody] UpdateIncidentStatusRequest request, CancellationToken ct)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateStatusAsync(incidentId, officerId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("officers/availability")]
    [Authorize(Roles = "medical_officer,security_officer,driver")]
    public async Task<IActionResult> UpdateAvailability(
        [FromBody] UpdateAvailabilityRequest request, CancellationToken ct)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateAvailabilityAsync(officerId, request, ct);
        return Ok(result);
    }

    [HttpPut("officers/location")]
    [Authorize(Roles = "medical_officer,security_officer,driver")]
    public async Task<IActionResult> UpdateLocation(
        [FromBody] UpdateLocationRequest request, CancellationToken ct)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateLocationAsync(officerId, request, ct);
        return Ok(result);
    }

    [HttpGet("officers/availability")]
    [Authorize(Roles = "medical_officer,security_officer,driver")]
    public async Task<IActionResult> GetMyAvailability(CancellationToken ct)
    {
        var officerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetAvailabilityAsync(officerId, ct);
        return Ok(result);
    }
}
