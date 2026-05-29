using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.MissingPersons.Application;
using System.Security.Claims;

namespace Nexum.Modules.MissingPersons.Api.Controllers;

[ApiController]
[Route("v1/alerts/missing-persons")]
[Authorize]
public sealed class MissingPersonsController : ControllerBase
{
    private readonly IMissingPersonService _service;
    public MissingPersonsController(IMissingPersonService service) => _service = service;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlertRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateAlertAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.GetAlertsAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{alertId:guid}")]
    public async Task<IActionResult> GetById(Guid alertId, CancellationToken ct)
    {
        var result = await _service.GetAlertAsync(alertId, ct);
        return result.Success ? Ok(result) : NotFound(result);
    }

    [HttpPut("{alertId:guid}/status")]
    [Authorize(Roles = "security_officer,admin")]
    public async Task<IActionResult> UpdateStatus(Guid alertId, [FromBody] object body, CancellationToken ct)
    {
        var status = (body as dynamic)?.status?.ToString() ?? "";
        var result = await _service.UpdateStatusAsync(alertId, status, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{alertId:guid}/sightings")]
    public async Task<IActionResult> CreateSighting(Guid alertId,
        [FromBody] CreateSightingRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateSightingAsync(userId, alertId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("{alertId:guid}/sightings")]
    [Authorize(Roles = "security_officer,admin")]
    public async Task<IActionResult> GetSightings(Guid alertId, CancellationToken ct)
    {
        var result = await _service.GetSightingsAsync(alertId, ct);
        return Ok(result);
    }
}
