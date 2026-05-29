using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.SharedKernel.Geofence;

namespace Nexum.Api.Infrastructure;

public sealed class GeofenceRepository : IGeofenceRepository
{
    private readonly NexumDbContext _db;
    public GeofenceRepository(NexumDbContext db) => _db = db;

    public async Task<Geometry> GetActivePolygonAsync(CancellationToken ct = default)
    {
        var zone = await _db.GeofenceZones
            .Where(z => z.IsActive)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No active geofence zone found in database.");
        return zone.Boundary;
    }
}
