using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nexum.Api.Hubs;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Geofence.Api.Controllers;

// ── DTOs ──────────────────────────────────────────────────────
public sealed record GeofenceZoneDto(
    Guid Id,
    string Name,
    string? Description,
    GeoJsonPolygon Boundary,
    bool IsActive,
    DateTime? ActivatedAt,
    DateTime CreatedAt
);

public sealed record GeoJsonPolygon(
    string Type,
    double[][][] Coordinates
);

public sealed record CreateGeofenceZoneRequest(
    [Required] string Name,
    string? Description,
    [Required] GeoJsonPolygon Boundary
);

public sealed record UpdateGeofenceZoneRequest(
    string? Name,
    string? Description,
    GeoJsonPolygon? Boundary
);

// ── Controller ────────────────────────────────────────────────
[ApiController]
[Route("v1/admin/geofence")]
[Authorize(Roles = Roles.Admin)]
public sealed class GeofenceAdminController : ControllerBase
{
    private readonly NexumDbContext _db;
    private readonly IGeofenceService _geofenceService;
    private readonly IHubContext<EmergencyHub> _hub;

    public GeofenceAdminController(
        NexumDbContext db,
        IGeofenceService geofenceService,
        IHubContext<EmergencyHub> hub)
    {
        _db = db;
        _geofenceService = geofenceService;
        _hub = hub;
    }

    // GET /v1/admin/geofence — list all zones
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var zones = await _db.GeofenceZones
            .OrderByDescending(z => z.IsActive)
            .ThenByDescending(z => z.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<GeofenceZoneDto>>.Ok(
            zones.Select(MapToDto).ToList()));
    }

    // GET /v1/admin/geofence/active — public, used by mobile app on startup
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var zone = await _db.GeofenceZones
            .FirstOrDefaultAsync(z => z.IsActive, ct);

        if (zone is null)
            return NotFound(ApiResponse<GeofenceZoneDto>.Fail(
                "NO_ACTIVE_ZONE", "No active geofence zone."));

        return Ok(ApiResponse<GeofenceZoneDto>.Ok(MapToDto(zone)));
    }

    // POST /v1/admin/geofence — create a new saved boundary
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateGeofenceZoneRequest request, CancellationToken ct)
    {
        var polygon = BuildPolygon(request.Boundary);
        if (polygon is null)
            return BadRequest(ApiResponse<GeofenceZoneDto>.Fail(
                "INVALID_BOUNDARY", "Boundary must be a valid GeoJSON Polygon."));

        var zone = new GeofenceZone
        {
            Id          = Guid.NewGuid(),
            Name        = request.Name,
            Description = request.Description,
            Boundary    = polygon,
            IsActive    = false,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        _db.GeofenceZones.Add(zone);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<GeofenceZoneDto>.Ok(MapToDto(zone)));
    }

    // PUT /v1/admin/geofence/{id} — update name, description, or boundary
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateGeofenceZoneRequest request, CancellationToken ct)
    {
        var zone = await _db.GeofenceZones.FindAsync([id], ct);
        if (zone is null)
            return NotFound(ApiResponse<bool>.Fail("NOT_FOUND", "Zone not found."));

        if (request.Name is not null)        zone.Name = request.Name;
        if (request.Description is not null) zone.Description = request.Description;
        if (request.Boundary is not null)
        {
            var polygon = BuildPolygon(request.Boundary);
            if (polygon is null)
                return BadRequest(ApiResponse<bool>.Fail(
                    "INVALID_BOUNDARY", "Invalid GeoJSON Polygon."));
            zone.Boundary  = polygon;
        }
        zone.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // If updating the active zone, refresh cache + broadcast
        if (zone.IsActive) await RefreshAndBroadcastAsync();

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // PUT /v1/admin/geofence/{id}/activate — set as the live boundary
    [HttpPut("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var zone = await _db.GeofenceZones.FindAsync([id], ct);
        if (zone is null)
            return NotFound(ApiResponse<bool>.Fail("NOT_FOUND", "Zone not found."));

        // Deactivate all others
        await _db.GeofenceZones
            .Where(z => z.IsActive && z.Id != id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(z => z.IsActive,   false)
                .SetProperty(z => z.UpdatedAt, DateTime.UtcNow), ct);

        zone.IsActive    = true;
        zone.ActivatedAt = DateTime.UtcNow;
        zone.UpdatedAt   = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Refresh in-memory polygon cache + push update to all clients
        await RefreshAndBroadcastAsync();

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // DELETE /v1/admin/geofence/{id} — remove an inactive zone
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var zone = await _db.GeofenceZones.FindAsync([id], ct);
        if (zone is null)
            return NotFound(ApiResponse<bool>.Fail("NOT_FOUND", "Zone not found."));

        if (zone.IsActive)
            return BadRequest(ApiResponse<bool>.Fail("ZONE_IS_ACTIVE",
                "Deactivate this zone by activating another before deleting."));

        _db.GeofenceZones.Remove(zone);
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Helpers ───────────────────────────────────────────────
    private async Task RefreshAndBroadcastAsync()
    {
        await _geofenceService.RefreshAsync();
        // Notify all connected clients (mobile + web) to re-fetch the active boundary
        await _hub.Clients.All.SendAsync("GeofenceBoundaryUpdated");
    }

    private static NetTopologySuite.Geometries.Geometry? BuildPolygon(GeoJsonPolygon boundary)
    {
        try
        {
            if (boundary.Type != "Polygon" || boundary.Coordinates?.Length == 0)
                return null;

            var ring = boundary.Coordinates![0]
                .Select(c => new NetTopologySuite.Geometries.Coordinate(c[0], c[1]))
                .ToArray();

            var factory = NetTopologySuite.NtsGeometryServices.Instance
                .CreateGeometryFactory(srid: 4326);
            return factory.CreatePolygon(ring);
        }
        catch { return null; }
    }

    private static GeofenceZoneDto MapToDto(GeofenceZone z)
    {
        double[][][] coords = [];
        if (z.Boundary is NetTopologySuite.Geometries.Polygon p)
        {
            coords =
            [
                p.ExteriorRing.Coordinates
                 .Select(c => new[] { c.X, c.Y })
                 .ToArray()
            ];
        }
        return new GeofenceZoneDto(
            z.Id, z.Name, z.Description,
            new GeoJsonPolygon("Polygon", coords),
            z.IsActive, z.ActivatedAt, z.CreatedAt);
    }
}
