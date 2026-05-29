using Microsoft.AspNetCore.Identity;
using Nexum.Modules.Auth.Domain.Enums;

namespace Nexum.Modules.Auth.Domain.Entities;

public sealed class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EstateOrZone { get; set; }
    public string? FcmToken { get; set; }
    public string? ApnsToken { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
