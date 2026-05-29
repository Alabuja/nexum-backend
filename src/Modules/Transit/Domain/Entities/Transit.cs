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
