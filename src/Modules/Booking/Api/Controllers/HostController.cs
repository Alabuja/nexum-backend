using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

// ── Host applications ─────────────────────────────────────────
[ApiController]
[Route("v1/host")]
[Authorize]
public sealed class HostController : ControllerBase
{
    private readonly IHostApplicationService _service;
    public HostController(IHostApplicationService service) => _service = service;

    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] HostApplicationRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.ApplyAsync(userId, request, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("application")]
    public async Task<IActionResult> MyApplication(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyApplicationAsync(userId, ct);
        return Ok(result);
    }
}

