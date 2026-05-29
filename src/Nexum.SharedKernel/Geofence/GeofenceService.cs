using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Nexum.SharedKernel.Interfaces;

namespace Nexum.SharedKernel.Geofence;

public sealed class GeofenceService : IGeofenceService
{
    // Use IServiceScopeFactory instead of IGeofenceRepository directly.
    // GeofenceService is singleton — it cannot hold a scoped DbContext.
    // IServiceScopeFactory is safe to inject into singletons.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeofenceService> _logger;
    private Geometry? _cachedPolygon;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GeofenceService(IServiceScopeFactory scopeFactory, ILogger<GeofenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Geometry GetActivePolygon()
    {
        if (_cachedPolygon is null)
            throw new InvalidOperationException("Geofence not loaded. Call RefreshAsync first.");
        return _cachedPolygon;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Create a short-lived scope to resolve the scoped repository
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IGeofenceRepository>();
            var polygon = await repository.GetActivePolygonAsync(ct);
            _cachedPolygon = polygon;
            _logger.LogInformation("Geofence polygon refreshed from database");
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool IsInsideCamp(double latitude, double longitude)
    {
        if (_cachedPolygon is null) return false;
        var point = new Point(longitude, latitude) { SRID = 4326 };
        return _cachedPolygon.Contains(point);
    }
}

public interface IGeofenceRepository
{
    Task<Geometry> GetActivePolygonAsync(CancellationToken ct = default);
}
