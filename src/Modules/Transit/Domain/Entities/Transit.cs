using NetTopologySuite.Geometries;

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
    public ShuttleStatus Status { get; set; } = ShuttleStatus.PendingApproval;
    public Geometry? CurrentLocation { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string VehicleType { get; set; } = "Bus";
    // Status values: PendingApproval, Available, EnRoute, Offline, Rejected
    public string? RejectionReason { get; set; }
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
public enum ShuttleStatus
{
    PendingApproval,  // Driver submitted vehicle, awaiting admin review
    Available,        // Vehicle approved + driver on duty, ready for requests
    EnRoute,          // Driver has accepted a request and is on the way
    Full,             // Vehicle has reached max passenger capacity
    Offline,          // Driver toggled off duty
    Rejected,         // Admin rejected the vehicle registration
}
public enum ShuttleRequestStatus { Pending, Assigned, PickedUp, Completed, Cancelled }

// WHERE EACH IS SET:
//
// PendingApproval  → VehicleService.SubmitVehicleAsync (driver submits vehicle)
//
// Available        → VehicleService.ApproveAsync (admin approves)
//                  → VehicleService.AdminCreateAsync (admin creates directly)
//                  → TransitService.UpdateDriverAvailabilityAsync (driver goes on duty)
//                  → After ride is completed (driver becomes available again)
//
// EnRoute          → TransitService.AcceptShuttleRequestAsync (driver accepts pickup)
//                    After a request is assigned, the vehicle is no longer available
//                    for other requests
//
// Full             → TransitService when the vehicle reaches Capacity passengers
//                    e.g. a 12-seat bus that has 12 passengers aboard
//                    Not shown in dispatch queries — won't receive new requests
//
// Offline          → TransitService.UpdateDriverAvailabilityAsync (driver goes off duty)
//
// Rejected         → VehicleService.RejectAsync (admin rejects vehicle)

