using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Booking.Domain.Enums;
using Nexum.Modules.Transit.Domain.Entities;

namespace Nexum.Api.Infrastructure;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        NexumDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        // ── 1. Roles ──────────────────────────────────────────
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // ── 2. Users (one per role) ───────────────────────────
        var users = new[]
        {
            new
            {
                FullName       = "Adebayo Okafor",
                Email          = "worshipper@nexum.ng",
                Password       = "Nexum@2026!",
                Role           = Roles.Worshipper,
                Phone          = "+2348011111111",
                EmergencyName  = "Funke Okafor",
                EmergencyPhone = "+2348022222222",
                Estate         = "Redemption Estate, Zone A",
            },
            new
            {
                FullName       = "Dr. Kemi Adeyemi",
                Email          = "medical@nexum.ng",
                Password       = "Nexum@2026!",
                Role           = Roles.MedicalOfficer,
                Phone          = "+2348033333333",
                EmergencyName  = "Tunde Adeyemi",
                EmergencyPhone = "+2348044444444",
                Estate         = "Medical Team Quarters",
            },
            new
            {
                FullName       = "Sgt. Emeka Nwachukwu",
                Email          = "security@nexum.ng",
                Password       = "Nexum@2026!",
                Role           = Roles.SecurityOfficer,
                Phone          = "+2348055555555",
                EmergencyName  = "Ngozi Nwachukwu",
                EmergencyPhone = "+2348066666666",
                Estate         = "Security Command Post",
            },
            new
            {
                FullName       = "Chukwudi Eze",
                Email          = "driver@nexum.ng",
                Password       = "Nexum@2026!",
                Role           = Roles.Driver,
                Phone          = "+2348077777777",
                EmergencyName  = "Ada Eze",
                EmergencyPhone = "+2348088888888",
                Estate         = "Transport Bay C",
            },
            new
            {
                FullName       = "Blessing Afolabi",
                Email          = "host@nexum.ng",
                Password       = "Nexum@2026!",
                Role           = Roles.Host,
                Phone          = "+2348099999999",
                EmergencyName  = "Rotimi Afolabi",
                EmergencyPhone = "+2348000000000",
                Estate         = "Afolabi Close, Zone B",
            },
            new
            {
                FullName       = "Admin Nexum",
                Email          = "admin@nexum.ng",
                Password       = "Nexum@Admin2026!",
                Role           = Roles.Admin,
                Phone          = "+2348012340000",
                EmergencyName  = "Support Team",
                EmergencyPhone = "+2348012340001",
                Estate         = "Administrative Block",
            },
        };

        // Keep track of host user ID for property seeding
        string? hostUserId = null;

        foreach (var u in users)
        {
            var existing = await userManager.FindByEmailAsync(u.Email);
            if (existing is not null)
            {
                if (u.Role == Roles.Host) hostUserId = existing.Id;
                continue;
            }

            var appUser = new ApplicationUser
            {
                UserName              = u.Email,
                Email                 = u.Email,
                FullName              = u.FullName,
                PhoneNumber           = u.Phone,
                EmergencyContactName  = u.EmergencyName,
                EmergencyContactPhone = u.EmergencyPhone,
                EstateOrZone          = u.Estate,
                EmailConfirmed        = true,
            };

            var result = await userManager.CreateAsync(appUser, u.Password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(appUser, u.Role);
                if (u.Role == Roles.Host) hostUserId = appUser.Id;
            }
        }

        // ── 3. Geofence (default Redemption City boundary) ───
        if (!db.GeofenceZones.Any(x => x.IsActive))
        {
            // Approximate polygon for Redemption City, Km 46 Lagos-Ibadan Expressway
            //var wkt = "POLYGON((3.3750 6.8300, 3.4050 6.8300, 3.4050 6.8500, 3.3750 6.8500, 3.3750 6.8300))";
            //var reader = new NetTopologySuite.IO.WKTReader();
            //db.GeofenceZones.Add(new()
            //{
            //    Name        = "Redemption City — Default Boundary",
            //    Description = "Default camp boundary. Update via admin portal with precise coordinates.",
            //    Boundary    = reader.Read(wkt),
            //    IsActive    = true,
            //    ActivatedAt = DateTime.UtcNow,
            //});


            // Approximate bounding box around Lokoja town center (Kogi State)
            // Centered on 7.8024°N, 6.7430°E — confirmed via OpenStreetMap Nominatim
            var wkt = "POLYGON((6.7230 7.7850, 6.7630 7.7850, 6.7630 8.0150, 6.7230 8.0150, 6.7230 7.7850))";
            var reader = new NetTopologySuite.IO.WKTReader();
            db.GeofenceZones.Add(new()
            {
                Name = "Lokoja — Default Boundary",
                Description = "Default boundary for Lokoja town area, Kogi State. Update via admin portal with precise coordinates.",
                Boundary = reader.Read(wkt),
                IsActive = true,
                ActivatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // ── 4. Seed 2 properties (requires host user) ────────
        if (hostUserId is not null && !db.Properties.Any())
        {
            await SeedPropertiesAsync(db, hostUserId);
        }

        // ── 5. Seed shuttle vehicle for driver ───────────────
        var driverUser = await userManager.FindByEmailAsync("driver@nexum.ng");
        if (driverUser is not null && !db.ShuttleVehicles.Any(v => v.DriverId == driverUser.Id))
        {
            db.ShuttleVehicles.Add(new()
            {
                Id = Guid.NewGuid(),
                DriverId = driverUser.Id,
                Registration = "LAG-001-NX",
                Capacity = 12,
                Status = ShuttleStatus.Available,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    // ── Property seed ─────────────────────────────────────────
    private static async Task SeedPropertiesAsync(NexumDbContext db, string hostId)
    {
        // Property 1 — Afolabi Guest House
        var property1 = new Property
        {
            Id          = Guid.NewGuid(),
            HostId      = hostId,
            Name        = "Afolabi Guest House",
            Description = "A clean, comfortable guest house located 5 minutes from the main auditorium. Features 24/7 security, reliable generator power, borehole water supply, and free WiFi. Perfect for families and individuals attending Holy Ghost Congress.",
            Address     = "12 Afolabi Close, Zone B, Redemption City, Km 46 Lagos-Ibadan Expressway",
            PhotoUrls   = [],
            Status      = PropertyStatus.Approved,
            LastSupervisedAt  = DateTime.UtcNow.AddMonths(-2),
            NextSupervisionDue = DateTime.UtcNow.AddMonths(16),
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        var room1a = new RoomType
        {
            Id            = Guid.NewGuid(),
            PropertyId    = property1.Id,
            Name          = "Standard Room",
            Description   = "Comfortable room with two single beds, air conditioning, and en-suite bathroom.",
            Capacity      = 2,
            PricePerNight = 8500,
            Amenities     = ["Air Conditioning", "WiFi", "En-Suite Bathroom", "Power Supply"],
            PhotoUrls     = [],
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow,
        };

        var room1b = new RoomType
        {
            Id            = Guid.NewGuid(),
            PropertyId    = property1.Id,
            Name          = "Family Suite",
            Description   = "Spacious suite with one double bed and two single beds. Ideal for families of up to 4.",
            Capacity      = 4,
            PricePerNight = 15000,
            Amenities     = ["Air Conditioning", "WiFi", "En-Suite Bathroom", "Power Supply", "Kitchenette"],
            PhotoUrls     = [],
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow,
        };

        // Property 2 — Canaan Lodge
        var property2 = new Property
        {
            Id          = Guid.NewGuid(),
            HostId      = hostId,
            Name        = "Canaan Lodge",
            Description = "Budget-friendly accommodation close to the Camp Arena. Shared facilities with hot water, clean bedding provided, and a communal kitchen. Ideal for solo worshippers and small groups on a budget.",
            Address     = "8 Canaan Street, Zone A, Redemption City, Km 46 Lagos-Ibadan Expressway",
            PhotoUrls   = [],
            Status      = PropertyStatus.Approved,
            LastSupervisedAt  = DateTime.UtcNow.AddMonths(-1),
            NextSupervisionDue = DateTime.UtcNow.AddMonths(17),
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        var room2a = new RoomType
        {
            Id            = Guid.NewGuid(),
            PropertyId    = property2.Id,
            Name          = "Single Room",
            Description   = "Compact single room with ceiling fan and shared bathroom facilities.",
            Capacity      = 1,
            PricePerNight = 4500,
            Amenities     = ["Ceiling Fan", "Clean Bedding", "Shared Bathroom", "Power Supply"],
            PhotoUrls     = [],
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow,
        };

        var room2b = new RoomType
        {
            Id            = Guid.NewGuid(),
            PropertyId    = property2.Id,
            Name          = "Twin Sharing Room",
            Description   = "Two single beds in a shared room, great for friends or siblings.",
            Capacity      = 2,
            PricePerNight = 6000,
            Amenities     = ["Ceiling Fan", "Clean Bedding", "Shared Bathroom", "Power Supply"],
            PhotoUrls     = [],
            IsActive      = true,
            CreatedAt     = DateTime.UtcNow,
        };

        db.Properties.AddRange(property1, property2);
        db.RoomTypes.AddRange(room1a, room1b, room2a, room2b);
        await db.SaveChangesAsync();

        // Seed 365 days of availability for each room type
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var availabilities = new List<RoomAvailability>();

        foreach (var (room, units) in new[] {
            (room1a, 3), (room1b, 2), (room2a, 5), (room2b, 4)
        })
        {
            for (int i = 0; i < 365; i++)
            {
                availabilities.Add(new RoomAvailability
                {
                    RoomTypeId      = room.Id,
                    Date            = today.AddDays(i),
                    TotalUnits      = units,
                    AvailableUnits  = units,
                    UpdatedAt       = DateTime.UtcNow,
                });
            }
        }

        db.RoomAvailabilities.AddRange(availabilities);
        await db.SaveChangesAsync();
    }
}
