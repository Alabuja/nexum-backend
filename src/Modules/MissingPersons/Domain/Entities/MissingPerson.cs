using System;
using NetTopologySuite.Geometries;

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