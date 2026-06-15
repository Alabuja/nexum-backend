using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Transit.Domain.Entities;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Nexum.Modules.Transit.Application.Services;

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

// ?? Service ???????????????????????????????????????????????????
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
                "?? New Pickup Request",
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
        var vehicle = await _db.ShuttleVehicles
        .FirstOrDefaultAsync(v => v.DriverId == driverId && v.IsActive, ct);

        if (vehicle is null)
            return ApiResponse<bool>.Fail("NO_VEHICLE",
                "No approved vehicle found. Submit your vehicle for admin approval first.");

        // Prevent going on duty if vehicle is not approved
        if (dto.IsAvailable && vehicle.Status == ShuttleStatus.PendingApproval)
            return ApiResponse<bool>.Fail("VEHICLE_PENDING",
                "Your vehicle is awaiting admin approval. You cannot go on duty yet.");

        if (dto.IsAvailable && vehicle.Status == ShuttleStatus.Rejected)
            return ApiResponse<bool>.Fail("VEHICLE_REJECTED",
                "Your vehicle registration was rejected. Please contact admin.");

        vehicle.Status = dto.IsAvailable ? ShuttleStatus.Available : ShuttleStatus.Offline;
        vehicle.UpdatedAt = DateTime.UtcNow;
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
            .Where(v => v.Status != ShuttleStatus.Offline)
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