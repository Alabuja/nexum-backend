using NetTopologySuite.Geometries;

namespace Nexum.Modules.Emergency.Domain.Entities;

public sealed class SosIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PatientId { get; set; } = string.Empty;
    public ReportType ReportType { get; set; }
    public Geometry PatientLocation { get; set; } = null!;
    public string? Description { get; set; }
    public string? AssignedOfficerId { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;
    public IncidentPriority Priority { get; set; } = IncidentPriority.Normal;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class IncidentUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public IncidentStatus StatusTo { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum ReportType { Medical, Security }
public enum IncidentStatus { Pending, Dispatched, EnRoute, Resolved, Cancelled }
public enum IncidentPriority { Low, Normal, High, Critical }
