using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Emergency.Domain.Entities;
using Nexum.Modules.Parking.Domain.Entities;
using Nexum.Modules.Transit.Domain.Entities;
using Nexum.SharedKernel.Interfaces;

namespace Nexum.Api.Jobs;

public sealed class EscalationJobs
{
    private readonly NexumDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ILogger<EscalationJobs> _logger;

    public EscalationJobs(NexumDbContext db, INotificationService notifications, ILogger<EscalationJobs> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    // Run every 1 minute: re-escalate pending SOS to all officers
    [AutomaticRetry(Attempts = 0)]
    public async Task EscalatePendingIncidentsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var pending = await _db.SosIncidents
            .Where(i => i.Status == IncidentStatus.Pending && i.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var incident in pending)
        {
            _logger.LogWarning("Incident {IncidentId} unassigned for 2+ minutes — escalating", incident.Id);

            var targetRole = incident.ReportType == ReportType.Medical ? "medical_officer" : "security_officer";
            var officerTokens = await _db.Users
                .Where(u => u.FcmToken != null)
                .Join(_db.UserRoles, u => u.Id, ur => ur.UserId, (u, ur) => new { u, ur })
                .Join(_db.Roles, x => x.ur.RoleId, r => r.Id, (x, r) => new { x.u, RoleName = r.Name })
                .Where(x => x.RoleName == targetRole && x.u.FcmToken != null)
                .Select(x => x.u.FcmToken!)
                .ToListAsync();

            if (officerTokens.Any())
            {
                await _notifications.SendPushToManyAsync(officerTokens,
                    "⚠️ Unattended Emergency",
                    $"{incident.ReportType} emergency has been waiting for 2+ minutes",
                    new Dictionary<string, string> { ["incidentId"] = incident.Id.ToString() });
            }
        }
    }

    // Run every 2 minutes: escalate unresolved blocking alerts
    [AutomaticRetry(Attempts = 0)]
    public async Task EscalateBlockingAlertsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var unresolved = await _db.BlockingAlerts
            .Where(a => a.Status == BlockingStatus.Notified && a.NotifiedAt < cutoff)
            .ToListAsync();

        foreach (var alert in unresolved)
        {
            alert.Status = BlockingStatus.Escalated;
            alert.EscalatedAt = DateTime.UtcNow;

            _logger.LogWarning("Blocking alert {AlertId} escalated to wardens", alert.Id);

            var blockerPin = alert.BlockerPinId is null
                ? null
                : await _db.ParkingPins.FindAsync([alert.BlockerPinId.Value]);
            var vehicleDetails = BuildBlockingVehicleDetails(blockerPin, alert.Note);

            var wardenTokens = await _db.Users
                .Where(u => u.FcmToken != null)
                .Join(_db.UserRoles, u => u.Id, ur => ur.UserId, (u, ur) => new { u, ur })
                .Join(_db.Roles, x => x.ur.RoleId, r => r.Id, (x, r) => new { x.u, RoleName = r.Name })
                .Where(x => x.RoleName == "security_officer" && x.u.FcmToken != null)
                .Select(x => x.u.FcmToken!)
                .ToListAsync();

            if (wardenTokens.Any())
            {
                await _notifications.SendPushToManyAsync(wardenTokens,
                    "🚗 Blocking Vehicle — Escalated",
                    $"Vehicle owner unresponsive. {vehicleDetails}",
                    new Dictionary<string, string>
                    {
                        ["blockingAlertId"] = alert.Id.ToString(),
                        ["licencePlate"] = blockerPin?.LicencePlate ?? "",
                        ["vehicleDescription"] = blockerPin?.VehicleDescription ?? "",
                        ["note"] = alert.Note ?? ""
                    });
            }
        }
        await _db.SaveChangesAsync();
    }

    private static string BuildBlockingVehicleDetails(ParkingPin? blockerPin, string? note)
    {
        var details = new[]
        {
            string.IsNullOrWhiteSpace(blockerPin?.LicencePlate) ? null : $"Plate: {blockerPin.LicencePlate}",
            string.IsNullOrWhiteSpace(blockerPin?.VehicleDescription) ? null : blockerPin.VehicleDescription,
            string.IsNullOrWhiteSpace(note) ? null : note
        }.Where(x => x is not null);

        var message = string.Join(" | ", details);
        return string.IsNullOrWhiteSpace(message)
            ? "Manual intervention needed."
            : $"{message}. Manual intervention needed.";
    }

    // Run every 5 minutes: cleanup stale officer locations
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupStaleOfficerLocationsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var stale = await _db.OfficerLocations
            .Where(ol => ol.LastSeenAt < cutoff && ol.IsAvailable)
            .ToListAsync();

        foreach (var loc in stale)
            loc.IsAvailable = false;

        if (stale.Any())
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Marked {Count} stale officer locations as unavailable", stale.Count);
        }
    }

    // Run every 30 seconds: retry pending shuttle requests
    [AutomaticRetry(Attempts = 0)]
    public async Task RetryPendingShuttleRequestsAsync()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        var pending = await _db.ShuttleRequests
            .Where(r => r.Status == ShuttleRequestStatus.Pending && r.CreatedAt < cutoff)
            .ToListAsync();

        if (!pending.Any()) return;

        var available = await _db.ShuttleVehicles
            .Where(v => v.Status == ShuttleStatus.Available &&
                        v.LastSeenAt >= DateTime.UtcNow.AddSeconds(-30))
            .ToListAsync();

        if (!available.Any()) return;

        foreach (var request in pending)
        {
            var nearest = available
                .Where(v => v.CurrentLocation is not null)
                .OrderBy(v => v.CurrentLocation!.Distance(request.PickupLocation))
                .FirstOrDefault();

            if (nearest is null) continue;

            nearest.Status = ShuttleStatus.EnRoute;
            request.AssignedVehicleId = nearest.Id;
            request.Status = ShuttleRequestStatus.Assigned;
            request.EstimatedArrivalMins = 5;
            request.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("Retry dispatch: request {RequestId} -> vehicle {VehicleId}",
                request.Id, nearest.Id);
        }

        await _db.SaveChangesAsync();
    }

    // Run every hour: cleanup expired OTPs
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupExpiredOtpsAsync()
    {
        var expired = await _db.OtpCodes
            .Where(o => o.ExpiresAt < DateTime.UtcNow && o.UsedAt == null)
            .ToListAsync();

        _db.OtpCodes.RemoveRange(expired);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Cleaned up {Count} expired OTP codes", expired.Count);
    }

    public static void RegisterRecurringJobs()
    {
        RecurringJob.AddOrUpdate<EscalationJobs>(
            "escalate-pending-incidents",
            j => j.EscalatePendingIncidentsAsync(),
            Cron.Minutely);

        RecurringJob.AddOrUpdate<EscalationJobs>(
            "escalate-blocking-alerts",
            j => j.EscalateBlockingAlertsAsync(),
            "*/2 * * * *");

        RecurringJob.AddOrUpdate<EscalationJobs>(
            "cleanup-stale-officers",
            j => j.CleanupStaleOfficerLocationsAsync(),
            "*/5 * * * *");

        RecurringJob.AddOrUpdate<EscalationJobs>(
            "retry-pending-shuttles",
            j => j.RetryPendingShuttleRequestsAsync(),
            "*/1 * * * *");

        RecurringJob.AddOrUpdate<EscalationJobs>(
            "cleanup-expired-otps",
            j => j.CleanupExpiredOtpsAsync(),
            Cron.Hourly);
    }
}
