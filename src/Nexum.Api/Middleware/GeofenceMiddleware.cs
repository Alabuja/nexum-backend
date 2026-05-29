using Nexum.SharedKernel.Interfaces;

namespace Nexum.Api.Middleware;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireInsideCampAttribute : Attribute { }

public sealed class GeofenceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GeofenceMiddleware> _logger;

    public GeofenceMiddleware(RequestDelegate next, ILogger<GeofenceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IGeofenceService geofence)
    {
        var endpoint = context.GetEndpoint();
        var requiresGeofence = endpoint?.Metadata.GetMetadata<RequireInsideCampAttribute>() is not null;

        if (requiresGeofence)
        {
            if (!double.TryParse(context.Request.Headers["X-Client-Lat"], out var lat) ||
                !double.TryParse(context.Request.Headers["X-Client-Lng"], out var lng))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = new { code = "LOCATION_MISSING", message = "X-Client-Lat and X-Client-Lng headers are required." }
                });
                return;
            }

            if (!geofence.IsInsideCamp(lat, lng))
            {
                _logger.LogWarning("Request from {Lat},{Lng} rejected — outside camp boundary", lat, lng);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    error = new { code = "OUTSIDE_CAMP_BOUNDARY", message = "This feature is only available inside Redemption City." }
                });
                return;
            }

            context.Items["InsideCamp"] = true;
            context.Items["ClientLat"] = lat;
            context.Items["ClientLng"] = lng;
        }

        await _next(context);
    }
}
