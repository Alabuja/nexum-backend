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
namespace Nexum.Modules.Transit.Domain.Entities;

public sealed class CampNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = string.Empty;
    public NodeType NodeType { get; set; }
    public Geometry Location { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public long? OsmId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CampEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }
    public Geometry Geom { get; set; } = null!;
    public double LengthM { get; set; }
    public string RoadType { get; set; } = "secondary";
    public bool AllowsVehicles { get; set; } = true;
    public bool AllowsPedestrians { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public long? OsmWayId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ShuttleVehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DriverId { get; set; } = string.Empty;
    public string Registration { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public ShuttleStatus Status { get; set; } = ShuttleStatus.OffDuty;
    public Geometry? CurrentLocation { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ShuttleRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PassengerId { get; set; } = string.Empty;
    public Geometry PickupLocation { get; set; } = null!;
    public Guid? PickupNodeId { get; set; }
    public Guid DestinationNodeId { get; set; }
    public Guid? AssignedVehicleId { get; set; }
    public ShuttleRequestStatus Status { get; set; } = ShuttleRequestStatus.Pending;
    public int? EstimatedArrivalMins { get; set; }
    public string? RouteGeoJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CongestionSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EdgeId { get; set; }
    public int DensityScore { get; set; }
    public bool IsFlagged { get; set; }
    public DateTime SnapshotAt { get; set; } = DateTime.UtcNow;
}

public enum NodeType { Gate, Junction, Landmark, ShuttleStop, ParkingArea }
public enum ShuttleStatus { Available, EnRoute, Full, OffDuty }
public enum ShuttleRequestStatus { Pending, Assigned, PickedUp, Completed, Cancelled }

// ── Application DTOs ──────────────────────────────────────────
namespace Nexum.Modules.Transit.Application;

public sealed record CreateShuttleRequestDto(
    [Required] double PickupLatitude,
    [Required] double PickupLongitude,
    [Required] Guid DestinationNodeId
);

public sealed record NodeDto(Guid Id, string Label, string NodeType,
    double Latitude, double Longitude, bool IsActive);

public sealed record EdgeDto(Guid Id, Guid FromNodeId, Guid ToNodeId,
    string RoadType, double LengthM, bool IsActive);

public sealed record ShuttleRequestDto(Guid Id, string PassengerId, string Status,
    int? EstimatedArrivalMins, string? RouteGeoJson, DateTime CreatedAt);

public sealed record ShuttleVehicleDto(Guid Id, string DriverId, string Registration,
    int Capacity, string Status, double? Latitude, double? Longitude, DateTime? LastSeenAt);

public sealed record UpdateDriverLocationDto([Required] double Latitude, [Required] double Longitude);
public sealed record UpdateDriverAvailabilityDto([Required] bool IsAvailable);

// ── Service ───────────────────────────────────────────────────
public interface ITransitService
{
    Task<ApiResponse<List<NodeDto>>> GetNodesAsync(string? nodeType = null, CancellationToken ct = default);
    Task<ApiResponse<List<EdgeDto>>> GetEdgesAsync(CancellationToken ct = default);
    Task<ApiResponse<ShuttleRequestDto>> CreateRequestAsync(string passengerId, CreateShuttleRequestDto dto, CancellationToken ct = default);
    Task<ApiResponse<ShuttleRequestDto?>> GetMyRequestAsync(string passengerId, CancellationToken ct = default);
    Task<ApiResponse<bool>> CancelRequestAsync(string passengerId, Guid requestId, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateDriverLocationAsync(string driverId, UpdateDriverLocationDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateDriverAvailabilityAsync(string driverId, UpdateDriverAvailabilityDto dto, CancellationToken ct = default);
    Task<ApiResponse<bool>> PickupPassengerAsync(string driverId, Guid requestId, CancellationToken ct = default);
    Task<ApiResponse<bool>> CompleteRideAsync(string driverId, Guid requestId, CancellationToken ct = default);
    Task<ApiResponse<List<ShuttleVehicleDto>>> GetLiveVehiclesAsync(CancellationToken ct = default);
    Task<ApiResponse<bool>> CloseEdgeAsync(Guid edgeId, CancellationToken ct = default);
    Task<ApiResponse<bool>> OpenEdgeAsync(Guid edgeId, CancellationToken ct = default);
}

public sealed class TransitService : ITransitService
{
    private readonly NexumDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<TransitService> _logger;

    public TransitService(NexumDbContext db, INotificationService notifications, ILogger<TransitService> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResponse<List<NodeDto>>> GetNodesAsync(string? nodeType = null, CancellationToken ct = default)
    {
        var query = _db.CampNodes.Where(n => n.IsActive);
        if (nodeType is not null && Enum.TryParse<NodeType>(nodeType, ignoreCase: true, out var nt))
            query = query.Where(n => n.NodeType == nt);

        var nodes = await query.ToListAsync(ct);
        return ApiResponse<List<NodeDto>>.Ok(nodes.Select(n =>
            new NodeDto(n.Id, n.Label, n.NodeType.ToString(),
                n.Location.Coordinate.Y, n.Location.Coordinate.X, n.IsActive)).ToList());
    }

    public async Task<ApiResponse<List<EdgeDto>>> GetEdgesAsync(CancellationToken ct = default)
    {
        var edges = await _db.CampEdges.Where(e => e.IsActive).ToListAsync(ct);
        return ApiResponse<List<EdgeDto>>.Ok(edges.Select(e =>
            new EdgeDto(e.Id, e.FromNodeId, e.ToNodeId, e.RoadType, e.LengthM, e.IsActive)).ToList());
    }

    public async Task<ApiResponse<ShuttleRequestDto>> CreateRequestAsync(string passengerId,
        CreateShuttleRequestDto dto, CancellationToken ct = default)
    {
        var destination = await _db.CampNodes.FindAsync([dto.DestinationNodeId], ct);
        if (destination is null)
            return ApiResponse<ShuttleRequestDto>.Fail("NODE_NOT_FOUND", "Destination stop not found.");

        // Find nearest pickup node
        var allStops = await _db.CampNodes
            .Where(n => n.NodeType == NodeType.ShuttleStop && n.IsActive)
            .ToListAsync(ct);
        var pickupPoint = new Point(dto.PickupLongitude, dto.PickupLatitude) { SRID = 4326 };
        var nearestStop = allStops.OrderBy(n => n.Location.Distance(pickupPoint)).FirstOrDefault();

        var request = new ShuttleRequest
        {
            PassengerId = passengerId,
            PickupLocation = pickupPoint,
            PickupNodeId = nearestStop?.Id,
            DestinationNodeId = dto.DestinationNodeId,
            Status = ShuttleRequestStatus.Pending
        };
        _db.ShuttleRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        // Auto-dispatch
        await AutoDispatchShuttleAsync(request, ct);

        return ApiResponse<ShuttleRequestDto>.Ok(MapRequestToDto(request));
    }

    private async Task AutoDispatchShuttleAsync(ShuttleRequest request, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        var available = await _db.ShuttleVehicles
            .Where(v => v.Status == ShuttleStatus.Available && v.LastSeenAt >= cutoff)
            .ToListAsync(ct);

        if (!available.Any())
        {
            _logger.LogWarning("No shuttles available for request {RequestId}", request.Id);
            return;
        }

        var nearest = available
            .Where(v => v.CurrentLocation is not null)
            .OrderBy(v => v.CurrentLocation!.Distance(request.PickupLocation))
            .First();

        nearest.Status = ShuttleStatus.EnRoute;
        request.AssignedVehicleId = nearest.Id;
        request.Status = ShuttleRequestStatus.Assigned;
        request.EstimatedArrivalMins = 5; // simplified; pgRouting would compute actual
        request.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        // Notify driver
        var driver = await _db.Users.FindAsync([nearest.DriverId], ct);
        if (driver?.FcmToken is not null)
        {
            await _notifications.SendPushAsync(driver.FcmToken,
                "🚌 New Pickup Request",
                "Passenger waiting nearby — tap to navigate",
                new Dictionary<string, string>
                {
                    ["requestId"] = request.Id.ToString(),
                    ["pickupLat"] = request.PickupLocation.Coordinate.Y.ToString(),
                    ["pickupLng"] = request.PickupLocation.Coordinate.X.ToString()
                }, ct);
        }
    }

    public async Task<ApiResponse<ShuttleRequestDto?>> GetMyRequestAsync(string passengerId,
        CancellationToken ct = default)
    {
        var req = await _db.ShuttleRequests
            .Where(r => r.PassengerId == passengerId &&
                        r.Status != ShuttleRequestStatus.Completed &&
                        r.Status != ShuttleRequestStatus.Cancelled)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return ApiResponse<ShuttleRequestDto?>.Ok(req is null ? null : MapRequestToDto(req));
    }

    public async Task<ApiResponse<bool>> CancelRequestAsync(string passengerId, Guid requestId,
        CancellationToken ct = default)
    {
        var req = await _db.ShuttleRequests.FindAsync([requestId], ct);
        if (req is null || req.PassengerId != passengerId)
            return ApiResponse<bool>.Fail("REQUEST_NOT_FOUND", "Request not found.");
        if (req.Status == ShuttleRequestStatus.Assigned)
            return ApiResponse<bool>.Fail("REQUEST_ALREADY_ASSIGNED", "Cannot cancel — already assigned.");

        req.Status = ShuttleRequestStatus.Cancelled;
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> UpdateDriverLocationAsync(string driverId,
        UpdateDriverLocationDto dto, CancellationToken ct = default)
    {
        var vehicle = await _db.ShuttleVehicles.FirstOrDefaultAsync(v => v.DriverId == driverId, ct);
        if (vehicle is null) return ApiResponse<bool>.Fail("VEHICLE_NOT_FOUND", "No vehicle assigned.");

        vehicle.CurrentLocation = new Point(dto.Longitude, dto.Latitude) { SRID = 4326 };
        vehicle.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> UpdateDriverAvailabilityAsync(string driverId,
        UpdateDriverAvailabilityDto dto, CancellationToken ct = default)
    {
        var vehicle = await _db.ShuttleVehicles.FirstOrDefaultAsync(v => v.DriverId == driverId, ct);
        if (vehicle is null) return ApiResponse<bool>.Fail("VEHICLE_NOT_FOUND", "No vehicle assigned.");

        vehicle.Status = dto.IsAvailable ? ShuttleStatus.Available : ShuttleStatus.OffDuty;
        vehicle.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> PickupPassengerAsync(string driverId, Guid requestId,
        CancellationToken ct = default)
    {
        var req = await _db.ShuttleRequests.FindAsync([requestId], ct);
        if (req is null) return ApiResponse<bool>.Fail("REQUEST_NOT_FOUND", "Request not found.");
        req.Status = ShuttleRequestStatus.PickedUp;
        req.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> CompleteRideAsync(string driverId, Guid requestId,
        CancellationToken ct = default)
    {
        var req = await _db.ShuttleRequests.FindAsync([requestId], ct);
        if (req is null) return ApiResponse<bool>.Fail("REQUEST_NOT_FOUND", "Request not found.");

        req.Status = ShuttleRequestStatus.Completed;
        req.UpdatedAt = DateTime.UtcNow;

        if (req.AssignedVehicleId.HasValue)
        {
            var vehicle = await _db.ShuttleVehicles.FindAsync([req.AssignedVehicleId.Value], ct);
            if (vehicle is not null) vehicle.Status = ShuttleStatus.Available;
        }
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<List<ShuttleVehicleDto>>> GetLiveVehiclesAsync(CancellationToken ct = default)
    {
        var vehicles = await _db.ShuttleVehicles
            .Where(v => v.Status != ShuttleStatus.OffDuty)
            .ToListAsync(ct);
        return ApiResponse<List<ShuttleVehicleDto>>.Ok(vehicles.Select(v =>
            new ShuttleVehicleDto(v.Id, v.DriverId, v.Registration, v.Capacity,
                v.Status.ToString(),
                v.CurrentLocation?.Coordinate.Y,
                v.CurrentLocation?.Coordinate.X,
                v.LastSeenAt)).ToList());
    }

    public async Task<ApiResponse<bool>> CloseEdgeAsync(Guid edgeId, CancellationToken ct = default)
    {
        var edge = await _db.CampEdges.FindAsync([edgeId], ct);
        if (edge is null) return ApiResponse<bool>.Fail("EDGE_NOT_FOUND", "Road segment not found.");
        if (!edge.IsActive) return ApiResponse<bool>.Fail("SEGMENT_ALREADY_CLOSED", "Segment already closed.");
        edge.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> OpenEdgeAsync(Guid edgeId, CancellationToken ct = default)
    {
        var edge = await _db.CampEdges.FindAsync([edgeId], ct);
        if (edge is null) return ApiResponse<bool>.Fail("EDGE_NOT_FOUND", "Road segment not found.");
        edge.IsActive = true;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    private static ShuttleRequestDto MapRequestToDto(ShuttleRequest r) =>
        new(r.Id, r.PassengerId, r.Status.ToString(), r.EstimatedArrivalMins, r.RouteGeoJson, r.CreatedAt);
}

// ── Controller ────────────────────────────────────────────────
namespace Nexum.Modules.Transit.Api.Controllers;

[ApiController]
[Route("v1/transit")]
public sealed class TransitController : ControllerBase
{
    private readonly ITransitService _service;
    public TransitController(ITransitService service) => _service = service;

    [HttpGet("network/nodes")]
    public async Task<IActionResult> GetNodes([FromQuery] string? nodeType, CancellationToken ct)
    {
        var result = await _service.GetNodesAsync(nodeType, ct);
        return Ok(result);
    }

    [HttpGet("network/edges")]
    public async Task<IActionResult> GetEdges(CancellationToken ct)
    {
        var result = await _service.GetEdgesAsync(ct);
        return Ok(result);
    }

    [HttpPost("shuttle-requests")]
    [Authorize]
    public async Task<IActionResult> CreateRequest([FromBody] CreateShuttleRequestDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CreateRequestAsync(userId, dto, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("shuttle-requests/mine")]
    [Authorize]
    public async Task<IActionResult> GetMyRequest(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.GetMyRequestAsync(userId, ct);
        return Ok(result);
    }

    [HttpPut("shuttle-requests/{requestId:guid}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelRequest(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CancelRequestAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpGet("vehicles/live")]
    [Authorize(Roles = "driver,admin")]
    public async Task<IActionResult> GetLiveVehicles(CancellationToken ct)
    {
        var result = await _service.GetLiveVehiclesAsync(ct);
        return Ok(result);
    }

    [HttpPut("drivers/availability")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> UpdateAvailability([FromBody] UpdateDriverAvailabilityDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateDriverAvailabilityAsync(userId, dto, ct);
        return Ok(result);
    }

    [HttpPut("drivers/location")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateDriverLocationDto dto, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.UpdateDriverLocationAsync(userId, dto, ct);
        return Ok(result);
    }

    [HttpPut("drivers/requests/{requestId:guid}/pickup")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> Pickup(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.PickupPassengerAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("drivers/requests/{requestId:guid}/complete")]
    [Authorize(Roles = "driver")]
    public async Task<IActionResult> Complete(Guid requestId, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _service.CompleteRideAsync(userId, requestId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("network/edges/{edgeId:guid}/close")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CloseEdge(Guid edgeId, CancellationToken ct)
    {
        var result = await _service.CloseEdgeAsync(edgeId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPut("network/edges/{edgeId:guid}/open")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> OpenEdge(Guid edgeId, CancellationToken ct)
    {
        var result = await _service.OpenEdgeAsync(edgeId, ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}
