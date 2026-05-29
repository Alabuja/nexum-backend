using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;
// ── Admin property management ─────────────────────────────────
[ApiController]
[Route("v1/admin/properties")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminPropertiesController : ControllerBase
{
    private readonly IPropertyService _service;
    public AdminPropertiesController(IPropertyService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await _service.AdminListAsync(page, pageSize, status, ct);
        return Ok(result);
    }

    [HttpPut("{propertyId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid propertyId, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(propertyId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{propertyId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid propertyId,
        [FromBody] RejectPropertyRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(propertyId, request.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{propertyId:guid}/supervise")]
    public async Task<IActionResult> RecordSupervision(Guid propertyId,
        [FromBody] RecordSupervisionRequest request, CancellationToken ct)
    {
        var result = await _service.RecordSupervisionAsync(propertyId, request.VisitDate, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}