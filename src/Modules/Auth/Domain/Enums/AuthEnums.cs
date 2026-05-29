namespace Nexum.Modules.Auth.Domain.Enums;

public enum AccountStatus { Active, Suspended, PendingApproval }

public static class Roles
{
    public const string Worshipper       = "worshipper";
    public const string MedicalOfficer   = "medical_officer";
    public const string SecurityOfficer  = "security_officer";
    public const string Driver           = "driver";
    public const string Admin            = "admin";

    public static readonly string[] All =
    [
        Worshipper, MedicalOfficer, SecurityOfficer, Driver, Admin
    ];
}
