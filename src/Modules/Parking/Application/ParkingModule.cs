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
namespace Nexum.Modules.Parking.Domain.Entities;

public sealed class ParkingPin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Geometry PinLocation { get; set; } = null!;
    public string? PhotoUrl { get; set; }
    public string? AreaLabel { get; set; }
    public string? VehicleDescription { get; set; }
    public string? LicencePlate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class BlockingAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReporterId { get; set; } = string.Empty;
    public Guid? BlockerPinId { get; set; }
    public Guid? ReporterPinId { get; set; }
    public Geometry ReporterLocation { get; set; } = null!;
    public string? Note { get; set; }
    public BlockingStatus Status { get; set; } = BlockingStatus.Pending;
    public DateTime? NotifiedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum BlockingStatus { Pending, Notified, Escalated, Resolved }

// ── Application ───────────────────────────────────────────────
namespace Nexum.Modules.Parking.Application;

public sealed record CreatePinRequest(
    [Required] double Latitude,
    [Required] double Longitude,
    string? AreaLabel,
    string? VehicleDescription,
    string? LicencePlate,
    string? PhotoUrl
);

public sealed record PinDto(
    Guid Id, string UserId, double Latitude, double Longitude,
    string? AreaLabel, string? VehicleDescription, string? LicencePlate,
    string? PhotoUrl, bool IsActive, DateTime CreatedAt
);

public sealed record CreateBlockingAlertRequest(
    [Required] double Latitude,
    [Required] double Longitude,
    string? Note
);

public sealed record BlockingAlertDto(
    Guid Id, string ReporterId, Guid? BlockerPinId,
    string Status, DateTime CreatedAt
);

public interface IParkingService
{
    Task<ApiResponse<PinDto>> CreatePinAsync(string userId, CreatePinRequest request, CancellationToken ct = default);
    Task<ApiResponse<PinDto?>> GetMyPinAsync(string userId, CancellationToken ct = default);
    Task<ApiResponse<bool>> DeactivatePinAsync(string userId, Guid pinId, CancellationToken ct = default);
    Task<ApiResponse<BlockingAlertDto>> CreateBlockingAlertAsync(string userId, CreateBlockingAlertRequest request, CancellationToken ct = default);
}

public sealed class ParkingService : IParkingService
{
    private readonly NexumDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<ParkingService> _logger;

    // 15 metres proximity for blocker matching
    private const double ProximityMetres = 15.0;

    public ParkingService(NexumDbContext db, INotificationService notifications, ILogger<ParkingService> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResponse<PinDto>> CreatePinAsync(string userId, CreatePinRequest request,
        CancellationToken ct = default)
    {
        var existing = await _db.ParkingPins
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);
        if (existing is not null)
            return ApiResponse<PinDto>.Fail("PIN_ALREADY_ACTIVE",
                "You already have an active parking pin. Remove it first.");

        var pin = new ParkingPin
        {
            UserId = userId,
            PinLocation = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
            AreaLabel = request.AreaLabel,
            VehicleDescription = request.VehicleDescription,
            LicencePlate = request.LicencePlate,
            PhotoUrl = request.PhotoUrl
        };
        _db.ParkingPins.Add(pin);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Parking pin {PinId} created for user {UserId}", pin.Id, userId);
        return ApiResponse<PinDto>.Ok(MapToDto(pin));
    }

    public async Task<ApiResponse<PinDto?>> GetMyPinAsync(string userId, CancellationToken ct = default)
    {
        var pin = await _db.ParkingPins
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);
        return ApiResponse<PinDto?>.Ok(pin is null ? null : MapToDto(pin));
    }

    public async Task<ApiResponse<bool>> DeactivatePinAsync(string userId, Guid pinId,
        CancellationToken ct = default)
    {
        var pin = await _db.ParkingPins.FindAsync([pinId], ct);
        if (pin is null || pin.UserId != userId)
            return ApiResponse<bool>.Fail("PIN_NOT_FOUND", "Parking pin not found.");
        if (!pin.IsActive)
            return ApiResponse<bool>.Fail("PIN_ALREADY_INACTIVE", "Pin is already inactive.");

        pin.IsActive = false;
        pin.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<BlockingAlertDto>> CreateBlockingAlertAsync(string userId,
        CreateBlockingAlertRequest request, CancellationToken ct = default)
    {
        var reporterLocation = new Point(request.Longitude, request.Latitude) { SRID = 4326 };

        // Find reporter's active pin
        var reporterPin = await _db.ParkingPins
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);

        // Find nearby pins excluding reporter's own (within 15m)
        var nearbyPins = await _db.ParkingPins
            .Where(p => p.IsActive && p.UserId != userId)
            .ToListAsync(ct);

        var blockerPin = nearbyPins
            .Where(p => p.PinLocation.Distance(reporterLocation) * 111139 <= ProximityMetres)
            .OrderBy(p => p.PinLocation.Distance(reporterLocation))
            .FirstOrDefault();

        var alert = new BlockingAlert
        {
            ReporterId = userId,
            BlockerPinId = blockerPin?.Id,
            ReporterPinId = reporterPin?.Id,
            ReporterLocation = reporterLocation,
            Note = request.Note,
            Status = blockerPin is null ? BlockingStatus.Escalated : BlockingStatus.Pending
        };
        _db.BlockingAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        if (blockerPin is not null)
        {
            // Notify blocking vehicle owner
            var owner = await _db.Users.FindAsync([blockerPin.UserId], ct);
            if (owner?.FcmToken is not null)
            {
                alert.NotifiedAt = DateTime.UtcNow;
                alert.Status = BlockingStatus.Notified;
                await _db.SaveChangesAsync(ct);

                await _notifications.SendPushAsync(owner.FcmToken,
                    "🚗 Your vehicle is blocking another",
                    "Please move your vehicle immediately.",
                    new Dictionary<string, string>
                    {
                        ["blockingAlertId"] = alert.Id.ToString(),
                        ["type"] = "blocking_vehicle"
                    }, ct);
            }
        }
        else
        {
            // No pin found — escalate to security immediately
            _logger.LogWarning("No blocker pin found for alert {AlertId} — escalating", alert.Id);
        }

        return ApiResponse<BlockingAlertDto>.Ok(
            new BlockingAlertDto(alert.Id, userId, alert.BlockerPinId,
                alert.Status.ToString(), alert.CreatedAt));
    }

    private static PinDto MapToDto(ParkingPin p) =>
        new(p.Id, p.UserId, p.PinLocation.Coordinate.Y, p.PinLocation.Coordinate.X,
            p.AreaLabel, p.VehicleDescription, p.LicencePlate, p.PhotoUrl, p.IsActive, p.CreatedAt);
}

// ── Controller ────────────────────────────────────────────────
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
