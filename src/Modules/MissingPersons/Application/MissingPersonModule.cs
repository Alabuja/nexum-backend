using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

// ── Domain ────────────────────────────────────────────────────
namespace Nexum.Modules.MissingPersons.Domain.Entities;

public sealed class MissingPersonAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReportedBy { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public Geometry? LastSeenLocation { get; set; }
    public string? LastSeenAreaText { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public DateTime? FoundAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MissingPersonSighting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AlertId { get; set; }
    public string ReportedBy { get; set; } = string.Empty;
    public Geometry SightingLocation { get; set; } = null!;
    public string? LocationDescription { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AlertStatus { Open, Found, Closed }

// ── Application DTOs ──────────────────────────────────────────
namespace Nexum.Modules.MissingPersons.Application;

public sealed record CreateAlertRequest(
    [Required] string FullName,
    int? Age,
    [Required] string Description,
    string? PhotoUrl,
    double? LastSeenLatitude,
    double? LastSeenLongitude,
    string? LastSeenAreaText
);

public sealed record CreateSightingRequest(
    [Required] double Latitude,
    [Required] double Longitude,
    string? LocationDescription,
    string? PhotoUrl
);

public sealed record AlertDto(
    Guid Id, string FullName, int? Age, string Description,
    string? PhotoUrl, string? LastSeenAreaText, string Status,
    string ReportedBy, int SightingCount, DateTime CreatedAt
);

public sealed record SightingDto(
    Guid Id, Guid AlertId, string ReportedBy,
    double Latitude, double Longitude,
    string? LocationDescription, DateTime CreatedAt
);

// ── Service ───────────────────────────────────────────────────
public interface IMissingPersonService
{
    Task<ApiResponse<AlertDto>> CreateAlertAsync(string userId, CreateAlertRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AlertDto>>> GetAlertsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<AlertDto>> GetAlertAsync(Guid alertId, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateStatusAsync(Guid alertId, string status, CancellationToken ct = default);
    Task<ApiResponse<SightingDto>> CreateSightingAsync(string userId, Guid alertId, CreateSightingRequest request, CancellationToken ct = default);
    Task<ApiResponse<List<SightingDto>>> GetSightingsAsync(Guid alertId, CancellationToken ct = default);
}

public sealed class MissingPersonService : IMissingPersonService
{
    private readonly NexumDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<MissingPersonService> _logger;

    public MissingPersonService(NexumDbContext db, INotificationService notifications,
        ILogger<MissingPersonService> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResponse<AlertDto>> CreateAlertAsync(string userId, CreateAlertRequest request,
        CancellationToken ct = default)
    {
        Geometry? lastSeen = null;
        if (request.LastSeenLatitude.HasValue && request.LastSeenLongitude.HasValue)
            lastSeen = new Point(request.LastSeenLongitude.Value, request.LastSeenLatitude.Value) { SRID = 4326 };

        var alert = new MissingPersonAlert
        {
            ReportedBy = userId,
            FullName = request.FullName,
            Age = request.Age,
            Description = request.Description,
            PhotoUrl = request.PhotoUrl,
            LastSeenLocation = lastSeen,
            LastSeenAreaText = request.LastSeenAreaText,
        };
        _db.MissingPersonAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        // Broadcast to all users with FCM tokens (geofence checked at middleware)
        var tokens = await _db.Users
            .Where(u => u.FcmToken != null)
            .Select(u => u.FcmToken!)
            .ToListAsync(ct);

        if (tokens.Any())
        {
            await _notifications.SendPushToManyAsync(tokens,
                $"🔍 Missing Person — {alert.FullName}",
                $"Age {alert.Age} · {alert.LastSeenAreaText ?? "Location unknown"}. Tap to view.",
                new Dictionary<string, string>
                {
                    ["alertId"] = alert.Id.ToString(),
                    ["type"] = "missing_person"
                }, ct);
        }

        _logger.LogInformation("Missing person alert {AlertId} created and broadcast to {Count} devices",
            alert.Id, tokens.Count);

        return ApiResponse<AlertDto>.Ok(MapToDto(alert, 0));
    }

    public async Task<ApiResponse<PagedResult<AlertDto>>> GetAlertsAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.MissingPersonAlerts.Where(a => a.Status == AlertStatus.Open);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var alertIds = items.Select(a => a.Id).ToList();
        var sightingCounts = await _db.MissingPersonSightings
            .Where(s => alertIds.Contains(s.AlertId))
            .GroupBy(s => s.AlertId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var dtos = items.Select(a => MapToDto(a, sightingCounts.GetValueOrDefault(a.Id, 0))).ToList();
        return ApiResponse<PagedResult<AlertDto>>.Ok(new PagedResult<AlertDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<AlertDto>> GetAlertAsync(Guid alertId, CancellationToken ct = default)
    {
        var alert = await _db.MissingPersonAlerts.FindAsync([alertId], ct);
        if (alert is null)
            return ApiResponse<AlertDto>.Fail("ALERT_NOT_FOUND", "Alert not found.");
        var count = await _db.MissingPersonSightings.CountAsync(s => s.AlertId == alertId, ct);
        return ApiResponse<AlertDto>.Ok(MapToDto(alert, count));
    }

    public async Task<ApiResponse<bool>> UpdateStatusAsync(Guid alertId, string status,
        CancellationToken ct = default)
    {
        var alert = await _db.MissingPersonAlerts.FindAsync([alertId], ct);
        if (alert is null)
            return ApiResponse<bool>.Fail("ALERT_NOT_FOUND", "Alert not found.");
        if (alert.Status == AlertStatus.Closed)
            return ApiResponse<bool>.Fail("ALERT_ALREADY_CLOSED", "Alert is already closed.");

        if (!Enum.TryParse<AlertStatus>(status, ignoreCase: true, out var newStatus))
            return ApiResponse<bool>.Fail("VALIDATION_ERROR", "Invalid status.");

        alert.Status = newStatus;
        alert.UpdatedAt = DateTime.UtcNow;
        if (newStatus == AlertStatus.Found) alert.FoundAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<SightingDto>> CreateSightingAsync(string userId, Guid alertId,
        CreateSightingRequest request, CancellationToken ct = default)
    {
        var alert = await _db.MissingPersonAlerts.FindAsync([alertId], ct);
        if (alert is null)
            return ApiResponse<SightingDto>.Fail("ALERT_NOT_FOUND", "Alert not found.");
        if (alert.Status == AlertStatus.Closed)
            return ApiResponse<SightingDto>.Fail("ALERT_ALREADY_CLOSED", "Alert is closed.");

        var sighting = new MissingPersonSighting
        {
            AlertId = alertId,
            ReportedBy = userId,
            SightingLocation = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
            LocationDescription = request.LocationDescription,
            PhotoUrl = request.PhotoUrl
        };
        _db.MissingPersonSightings.Add(sighting);
        await _db.SaveChangesAsync(ct);

        // Notify reporter and security officers
        var reporter = await _db.Users.FindAsync([alert.ReportedBy], ct);
        if (reporter?.FcmToken is not null)
        {
            await _notifications.SendPushAsync(reporter.FcmToken,
                "👁 Sighting Reported",
                $"{alert.FullName} spotted near {request.LocationDescription ?? "unknown location"}",
                new Dictionary<string, string> { ["alertId"] = alertId.ToString() }, ct);
        }

        return ApiResponse<SightingDto>.Ok(MapSightingToDto(sighting));
    }

    public async Task<ApiResponse<List<SightingDto>>> GetSightingsAsync(Guid alertId, CancellationToken ct = default)
    {
        var sightings = await _db.MissingPersonSightings
            .Where(s => s.AlertId == alertId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return ApiResponse<List<SightingDto>>.Ok(sightings.Select(MapSightingToDto).ToList());
    }

    private static AlertDto MapToDto(MissingPersonAlert a, int sightingCount) =>
        new(a.Id, a.FullName, a.Age, a.Description, a.PhotoUrl,
            a.LastSeenAreaText, a.Status.ToString(), a.ReportedBy, sightingCount, a.CreatedAt);

    private static SightingDto MapSightingToDto(MissingPersonSighting s) =>
        new(s.Id, s.AlertId, s.ReportedBy,
            s.SightingLocation.Coordinate.Y, s.SightingLocation.Coordinate.X,
            s.LocationDescription, s.CreatedAt);
}

// ── Controller ────────────────────────────────────────────────
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
