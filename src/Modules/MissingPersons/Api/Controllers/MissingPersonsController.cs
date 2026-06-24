using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Booking.Application.Services;
using Nexum.Modules.MissingPersons.Application;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Nexum.Modules.MissingPersons.Api.Controllers;

[ApiController]
[Route("v1/alerts/missing-persons")]
[Authorize]
public sealed class MissingPersonsController : ControllerBase
{
    private readonly IMissingPersonService _service;
    private readonly IImageUploadService _uploadService;

    public MissingPersonsController(IMissingPersonService service, IImageUploadService uploadService)
    {
        _service = service;
        _uploadService = uploadService;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateMissingPersonAlertForm request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        string? photoUrl = null;

        if (request.Photo is not null)
        {
            var upload = await _uploadService.UploadImageAsync(
                $"nexum/missing-persons/{Guid.NewGuid()}", request.Photo, ct);
            if (!upload.Success) return BadRequest(upload);
            photoUrl = upload.Data;
        }

        var createRequest = new CreateAlertRequest(
            request.FullName,
            request.Age,
            request.Description,
            photoUrl,
            request.LastSeenLatitude,
            request.LastSeenLongitude,
            request.LastSeenAreaText);

        var result = await _service.CreateAlertAsync(userId, createRequest, ct);
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

public sealed class CreateMissingPersonAlertForm
{
    [Required] [FromForm] public string FullName { get; set; } = string.Empty;
    [FromForm] public int? Age { get; set; }
    [Required] [FromForm] public string Description { get; set; } = string.Empty;
    [FromForm] public double? LastSeenLatitude { get; set; }
    [FromForm] public double? LastSeenLongitude { get; set; }
    [FromForm] public string? LastSeenAreaText { get; set; }
    [FromForm] public IFormFile? Photo { get; set; }
}
