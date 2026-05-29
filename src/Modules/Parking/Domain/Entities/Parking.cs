using NetTopologySuite.Geometries;

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
