using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nexum.Modules.Auth.Domain.Enums;

namespace Nexum.Api.Hubs;

[Authorize]
public sealed class EmergencyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role is Roles.MedicalOfficer or Roles.SecurityOfficer or Roles.Admin)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"officers_{role}");

        await Groups.AddToGroupAsync(Context.ConnectionId, "camp_users");
        await base.OnConnectedAsync();
    }

    public async Task UpdateLocation(double latitude, double longitude)
    {
        var userId = Context.UserIdentifier;
        await Clients.Group("admin").SendAsync("OfficerLocationUpdate",
            new { UserId = userId, Latitude = latitude, Longitude = longitude, Timestamp = DateTime.UtcNow });
    }
}

[Authorize]
public sealed class MissingPersonsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "camp_users");
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role is Roles.SecurityOfficer or Roles.Admin)
            await Groups.AddToGroupAsync(Context.ConnectionId, "security_officers");
        await base.OnConnectedAsync();
    }
}

[Authorize]
public sealed class ShuttleHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role == Roles.Driver)
            await Groups.AddToGroupAsync(Context.ConnectionId, "drivers");
        if (role == Roles.Admin)
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        await Groups.AddToGroupAsync(Context.ConnectionId, "camp_users");
        await base.OnConnectedAsync();
    }

    public async Task UpdateVehicleLocation(double latitude, double longitude)
    {
        var userId = Context.UserIdentifier;
        await Clients.Group("camp_users").SendAsync("ShuttleLocationUpdate",
            new { DriverId = userId, Latitude = latitude, Longitude = longitude, Timestamp = DateTime.UtcNow });
    }
}

// SignalR notification helpers — injected where needed
public interface IEmergencyHubService
{
    Task NotifyNewIncidentAsync(object incident);
    Task NotifyIncidentStatusChangedAsync(object update);
    Task NotifyOfficerLocationAsync(string officerId, double lat, double lng);
}

public interface IMissingPersonsHubService
{
    Task BroadcastNewAlertAsync(object alert);
    Task NotifySightingAsync(object sighting);
    Task NotifyAlertStatusChangedAsync(object update);
}

public interface IShuttleHubService
{
    Task NotifyNewRequestAsync(object request);
    Task NotifyShuttleAssignedAsync(string passengerId, object assignment);
    Task NotifyCongestionAlertAsync(object congestion);
}

public sealed class EmergencyHubService : IEmergencyHubService
{
    private readonly IHubContext<EmergencyHub> _hub;
    public EmergencyHubService(IHubContext<EmergencyHub> hub) => _hub = hub;

    public Task NotifyNewIncidentAsync(object incident) =>
        _hub.Clients.Group("officers_medical_officer").SendAsync("NewIncidentAlert", incident);

    public Task NotifyIncidentStatusChangedAsync(object update) =>
        _hub.Clients.Group("camp_users").SendAsync("IncidentStatusChanged", update);

    public Task NotifyOfficerLocationAsync(string officerId, double lat, double lng) =>
        _hub.Clients.Group("camp_users").SendAsync("OfficerLocationUpdate",
            new { OfficerId = officerId, Latitude = lat, Longitude = lng });
}

public sealed class MissingPersonsHubService : IMissingPersonsHubService
{
    private readonly IHubContext<MissingPersonsHub> _hub;
    public MissingPersonsHubService(IHubContext<MissingPersonsHub> hub) => _hub = hub;

    public Task BroadcastNewAlertAsync(object alert) =>
        _hub.Clients.Group("camp_users").SendAsync("NewMissingPersonAlert", alert);

    public Task NotifySightingAsync(object sighting) =>
        _hub.Clients.Group("security_officers").SendAsync("AlertSightingReceived", sighting);

    public Task NotifyAlertStatusChangedAsync(object update) =>
        _hub.Clients.Group("camp_users").SendAsync("AlertStatusUpdated", update);
}

public sealed class ShuttleHubService : IShuttleHubService
{
    private readonly IHubContext<ShuttleHub> _hub;
    public ShuttleHubService(IHubContext<ShuttleHub> hub) => _hub = hub;

    public Task NotifyNewRequestAsync(object request) =>
        _hub.Clients.Group("drivers").SendAsync("NewShuttleRequest", request);

    public Task NotifyShuttleAssignedAsync(string passengerId, object assignment) =>
        _hub.Clients.User(passengerId).SendAsync("ShuttleAssigned", assignment);

    public Task NotifyCongestionAlertAsync(object congestion) =>
        _hub.Clients.Group("drivers").SendAsync("CongestionAlert", congestion);
}
