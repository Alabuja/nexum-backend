using NetTopologySuite.Geometries;

namespace Nexum.SharedKernel.Interfaces;

public interface IGeofenceService
{
    Geometry GetActivePolygon();
    Task RefreshAsync(CancellationToken ct = default);
    bool IsInsideCamp(double latitude, double longitude);
}

public interface INotificationService
{
    Task SendPushAsync(string fcmToken, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken ct = default);
    Task SendPushToManyAsync(IEnumerable<string> fcmTokens, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken ct = default);
}

public interface IEmailService
{
    Task SendAsync(string toEmail, string toName, string subject,
        string htmlBody, CancellationToken ct = default);
}

public interface ICurrentUser
{
    string Id { get; }
    string Email { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
}
