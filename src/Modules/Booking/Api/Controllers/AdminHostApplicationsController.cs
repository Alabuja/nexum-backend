using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Properties (public browse + host management) ──────────────

[ApiController]
[Route("v1/admin/host-applications")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminHostApplicationsController : ControllerBase
{
    private readonly IHostApplicationService _service;
    public AdminHostApplicationsController(IHostApplicationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpPut("{applicationId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid applicationId, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(applicationId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{applicationId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid applicationId,
        [FromBody] RejectHostRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(applicationId, request.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
