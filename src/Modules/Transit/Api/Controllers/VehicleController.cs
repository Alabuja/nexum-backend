using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Transit.Application.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Nexum.Modules.Transit.Api.Controllers;

// ── Request DTOs ──────────────────────────────────────────────
public sealed record SubmitVehicleHttpRequest(
    [Required, StringLength(20, MinimumLength = 5)] string Registration,
    [Required, Range(1, 100)] int Capacity,
    string? VehicleType
);

public sealed record AdminCreateVehicleHttpRequest(
    [Required] string DriverId,
    [Required, StringLength(20, MinimumLength = 5)] string Registration,
    [Required, Range(1, 100)] int Capacity,
    string? VehicleType
);

public sealed record RejectVehicleHttpRequest([Required] string Reason);

// ── Driver controller ─────────────────────────────────────────
[ApiController]
[Route("v1/driver/vehicle")]
[Authorize(Roles = Roles.Driver)]
public sealed class DriverVehicleController : ControllerBase
{
    private readonly IVehicleService _service;
    public DriverVehicleController(IVehicleService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetMyVehicle(CancellationToken ct)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyVehicleAsync(driverId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitVehicleHttpRequest request, CancellationToken ct)
    {
        var driverId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.SubmitVehicleAsync(driverId,
            new SubmitVehicleRequest(request.Registration, request.Capacity, request.VehicleType), ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ── Admin controller ──────────────────────────────────────────
[ApiController]
[Route("v1/admin/vehicles")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminVehicleController : ControllerBase
{
    private readonly IVehicleService _service;
    public AdminVehicleController(IVehicleService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(status, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await _service.ApproveAsync(id, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody] RejectVehicleHttpRequest request, CancellationToken ct)
    {
        var result = await _service.RejectAsync(id, request.Reason, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost]
    public async Task<IActionResult> AdminCreate(
        [FromBody] AdminCreateVehicleHttpRequest request, CancellationToken ct)
    {
        var result = await _service.AdminCreateAsync(
            new AdminCreateVehicleRequest(request.DriverId, request.Registration,
                request.Capacity, request.VehicleType), ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
