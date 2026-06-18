using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Nexum.Modules.Auth.Domain.Entities;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Emergency.Domain.Entities;
using Nexum.Modules.MissingPersons.Domain.Entities;
using Nexum.Modules.Parking.Domain.Entities;
using Nexum.Modules.Transit.Domain.Entities;
using Nexum.SharedKernel.Geofence;

namespace Nexum.Modules.Auth.Infrastructure.Persistence;

public sealed class NexumDbContext : IdentityDbContext<ApplicationUser>
{
    public NexumDbContext(DbContextOptions<NexumDbContext> options) : base(options) { }

    // Auth tables
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();

    // Shared
    public DbSet<GeofenceZone> GeofenceZones => Set<GeofenceZone>();
    public DbSet<OfficerLocation> OfficerLocations => Set<OfficerLocation>();

    // Missing persons
    public DbSet<MissingPersonAlert> MissingPersonAlerts => Set<MissingPersonAlert>();
    public DbSet<MissingPersonSighting> MissingPersonSightings => Set<MissingPersonSighting>();

    // Emergency
    public DbSet<SosIncident> SosIncidents => Set<SosIncident>();
    public DbSet<IncidentUpdate> IncidentUpdates => Set<IncidentUpdate>();

    // Parking
    public DbSet<ParkingPin> ParkingPins => Set<ParkingPin>();
    public DbSet<BlockingAlert> BlockingAlerts => Set<BlockingAlert>();

    // Transit
    public DbSet<CampNode> CampNodes => Set<CampNode>();
    public DbSet<CampEdge> CampEdges => Set<CampEdge>();
    public DbSet<ShuttleVehicle> ShuttleVehicles => Set<ShuttleVehicle>();
    public DbSet<ShuttleRequest> ShuttleRequests => Set<ShuttleRequest>();
    public DbSet<CongestionSnapshot> CongestionSnapshots => Set<CongestionSnapshot>();

    // Booking
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<RoomAvailability> RoomAvailabilities => Set<RoomAvailability>();
    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<HostApplication> HostApplications => Set<HostApplication>();
    public DbSet<HostBankAccount> HostBankAccounts => Set<HostBankAccount>();
    public DbSet<BookingTransfer> BookingTransfers => Set<BookingTransfer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("postgis");
        builder.HasPostgresExtension("pgrouting");
        builder.ApplyConfigurationsFromAssembly(typeof(NexumDbContext).Assembly);

        builder.Entity<GeofenceZone>(e =>
        {
            e.HasKey(z => z.Id);
            e.Property(z => z.Boundary)
             .HasColumnType("geometry(Polygon, 4326)");
            e.HasIndex(z => z.IsActive)
             .HasFilter("\"IsActive\" = true"); // partial index — fast active lookup
        });

    }
}

// Shared entity kept here for simplicity
public sealed class GeofenceZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Geometry Boundary { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class OfficerLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public Geometry? Location { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
