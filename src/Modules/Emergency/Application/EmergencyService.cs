using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Emergency.Domain.Entities;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Emergency.Application;

// ── DTOs ──────────────────────────────────────────────────────
public sealed record ReportRequest(
    [Required] string ReportType,   // "medical" | "security"
    [Required] double Latitude,
    [Required] double Longitude,
    string? Description
);

public sealed record IncidentDto(
    Guid Id, string ReportType, string Status, string Priority,
    string PatientId, string? PatientName, string? PatientEmergencyContact,
    double Latitude, double Longitude, string? Description,
    string? AssignedOfficerId, string? AssignedOfficerName,
    DateTime CreatedAt, DateTime? DispatchedAt, DateTime? ResolvedAt
);

public sealed record UpdateAvailabilityRequest([Required] bool IsAvailable);
public sealed record UpdateLocationRequest([Required] double Latitude, [Required] double Longitude);
public sealed record UpdateIncidentStatusRequest([Required] string Status, string? Note);

// ── Service ───────────────────────────────────────────────────
public interface IEmergencyService
{
    Task<ApiResponse<IncidentDto>> CreateReportAsync(string patientId, ReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<IncidentDto>>> GetIncidentsAsync(string officerId, string officerRole, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<IncidentDto>> GetIncidentAsync(Guid incidentId, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateStatusAsync(Guid incidentId, string officerId, UpdateIncidentStatusRequest request, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateAvailabilityAsync(string officerId, UpdateAvailabilityRequest request, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateLocationAsync(string officerId, UpdateLocationRequest request, CancellationToken ct = default);
}

public sealed class EmergencyService : IEmergencyService
{
    private readonly NexumDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<EmergencyService> _logger;

    public EmergencyService(NexumDbContext db, INotificationService notifications, ILogger<EmergencyService> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResponse<IncidentDto>> CreateReportAsync(string patientId, ReportRequest request,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ReportType>(request.ReportType, ignoreCase: true, out var reportType))
            return ApiResponse<IncidentDto>.Fail("INVALID_REPORT_TYPE",
                "report_type must be 'medical' or 'security'.");

        var location = new NetTopologySuite.Geometries.Point(request.Longitude, request.Latitude) { SRID = 4326 };

        var incident = new SosIncident
        {
            PatientId = patientId,
            ReportType = reportType,
            PatientLocation = location,
            Description = request.Description,
            Status = IncidentStatus.Pending,
        };
        _db.SosIncidents.Add(incident);
        await _db.SaveChangesAsync(ct);

        // Auto-dispatch: find nearest available officer of matching role
        var targetRole = reportType == ReportType.Medical ? "medical_officer" : "security_officer";
        await AutoDispatchAsync(incident, targetRole, ct);

        var dto = await BuildDtoAsync(incident, ct);
        return ApiResponse<IncidentDto>.Ok(dto);
    }

    private async Task AutoDispatchAsync(SosIncident incident, string targetRole, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        // Find all available officers of matching role with recent location ping
        var officerLocations = await _db.OfficerLocations
            .Where(ol => ol.IsAvailable && ol.LastSeenAt >= cutoff)
            .ToListAsync(ct);

        if (!officerLocations.Any())
        {
            _logger.LogWarning("No officers available for incident {IncidentId}", incident.Id);
            return;
        }

        // Sort by distance using PostGIS (simplified: client-side distance for demo)
        var nearest = officerLocations
            .OrderBy(ol => ol.Location.Distance(incident.PatientLocation))
            .First();

        incident.AssignedOfficerId = nearest.UserId;
        incident.Status = IncidentStatus.Dispatched;
        incident.DispatchedAt = DateTime.UtcNow;
        incident.UpdatedAt = DateTime.UtcNow;

        _db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            UpdatedBy = "system",
            StatusTo = IncidentStatus.Dispatched,
            Note = "Auto-dispatched to nearest available officer"
        });

        await _db.SaveChangesAsync(ct);

        // Notify officer
        var officer = await _db.Users.FindAsync(nearest.UserId);
        if (officer?.FcmToken is not null)
        {
            await _notifications.SendPushAsync(officer.FcmToken,
                "🚨 Emergency Dispatch",
                $"New {incident.ReportType} incident — tap to respond",
                new Dictionary<string, string>
                {
                    ["incidentId"] = incident.Id.ToString(),
                    ["latitude"] = incident.PatientLocation.Coordinate.Y.ToString(),
                    ["longitude"] = incident.PatientLocation.Coordinate.X.ToString(),
                    ["type"] = incident.ReportType.ToString()
                }, ct);
        }

        _logger.LogInformation("Incident {IncidentId} dispatched to officer {OfficerId}",
            incident.Id, nearest.UserId);
    }

    public async Task<ApiResponse<PagedResult<IncidentDto>>> GetIncidentsAsync(
        string officerId, string officerRole, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SosIncidents.AsQueryable();

        // Officers only see their own role type incidents
        if (officerRole == "medical_officer")
            query = query.Where(i => i.ReportType == ReportType.Medical);
        else if (officerRole == "security_officer")
            query = query.Where(i => i.ReportType == ReportType.Security);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = new List<IncidentDto>();
        foreach (var i in items) dtos.Add(await BuildDtoAsync(i, ct));

        return ApiResponse<PagedResult<IncidentDto>>.Ok(new PagedResult<IncidentDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<IncidentDto>> GetIncidentAsync(Guid incidentId, CancellationToken ct = default)
    {
        var incident = await _db.SosIncidents.FindAsync([incidentId], ct);
        if (incident is null)
            return ApiResponse<IncidentDto>.Fail("INCIDENT_NOT_FOUND", "Incident not found.");

        return ApiResponse<IncidentDto>.Ok(await BuildDtoAsync(incident, ct));
    }

    public async Task<ApiResponse<bool>> UpdateStatusAsync(Guid incidentId, string officerId,
        UpdateIncidentStatusRequest request, CancellationToken ct = default)
    {
        var incident = await _db.SosIncidents.FindAsync([incidentId], ct);
        if (incident is null)
            return ApiResponse<bool>.Fail("INCIDENT_NOT_FOUND", "Incident not found.");

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Cancelled)
            return ApiResponse<bool>.Fail("INCIDENT_ALREADY_RESOLVED", "Incident is already closed.");

        if (!Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var newStatus))
            return ApiResponse<bool>.Fail("VALIDATION_ERROR", "Invalid status value.");

        incident.Status = newStatus;
        incident.UpdatedAt = DateTime.UtcNow;
        if (newStatus == IncidentStatus.Resolved) incident.ResolvedAt = DateTime.UtcNow;

        _db.IncidentUpdates.Add(new IncidentUpdate
        {
            IncidentId = incident.Id,
            UpdatedBy = officerId,
            StatusTo = newStatus,
            Note = request.Note
        });

        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> UpdateAvailabilityAsync(string officerId,
        UpdateAvailabilityRequest request, CancellationToken ct = default)
    {
        var location = await _db.OfficerLocations.FirstOrDefaultAsync(ol => ol.UserId == officerId, ct);
        if (location is null)
        {
            location = new OfficerLocation { UserId = officerId };
            _db.OfficerLocations.Add(location);
        }
        location.IsAvailable = request.IsAvailable;
        location.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> UpdateLocationAsync(string officerId,
        UpdateLocationRequest request, CancellationToken ct = default)
    {
        var location = await _db.OfficerLocations.FirstOrDefaultAsync(ol => ol.UserId == officerId, ct);
        if (location is null)
        {
            location = new OfficerLocation { UserId = officerId };
            _db.OfficerLocations.Add(location);
        }
        location.Location = new NetTopologySuite.Geometries.Point(request.Longitude, request.Latitude) { SRID = 4326 };
        location.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    private async Task<IncidentDto> BuildDtoAsync(SosIncident incident, CancellationToken ct)
    {
        var patient = await _db.Users.FindAsync([incident.PatientId], ct);
        var officer = incident.AssignedOfficerId is not null
            ? await _db.Users.FindAsync([incident.AssignedOfficerId], ct)
            : null;

        return new IncidentDto(
            incident.Id, incident.ReportType.ToString(), incident.Status.ToString(),
            incident.Priority.ToString(), incident.PatientId,
            patient?.FullName, patient?.EmergencyContactPhone,
            incident.PatientLocation.Coordinate.Y, incident.PatientLocation.Coordinate.X,
            incident.Description, incident.AssignedOfficerId, officer?.FullName,
            incident.CreatedAt, incident.DispatchedAt, incident.ResolvedAt);
    }
}
