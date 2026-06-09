using Nexum.Modules.Booking.Domain.Enums;

namespace Nexum.Modules.Booking.Domain.Entities;

public sealed class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HostId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<string> PhotoUrls { get; set; } = [];
    public PropertyStatus Status { get; set; } = PropertyStatus.PendingApproval;
    public string? RejectionReason { get; set; }
    public DateTime? LastSupervisedAt { get; set; }
    public DateTime? NextSupervisionDue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RoomType> RoomTypes { get; set; } = [];
}

public sealed class RoomType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public decimal PricePerNight { get; set; }
    public List<string> Amenities { get; set; } = [];
    public List<string> PhotoUrls { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int TotalUnits { get; set; } = 1;
    public Property Property { get; set; } = null!;
    public List<RoomAvailability> Availability { get; set; } = [];
}

public sealed class RoomAvailability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoomTypeId { get; set; }
    public DateOnly Date { get; set; }
    public int TotalUnits { get; set; }
    public int AvailableUnits { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public RoomType RoomType { get; set; } = null!;
}

public sealed class BookingEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GuestId { get; set; } = string.Empty;
    public Guid PropertyId { get; set; }
    public Guid RoomTypeId { get; set; }
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int NumNights { get; set; }
    public decimal TotalAmount { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.PendingPayment;
    public string? ConfirmationCode { get; set; }
    public string? PaystackReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Property Property { get; set; } = null!;
    public RoomType RoomType { get; set; } = null!;
}

public sealed class HostApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? IdentityDocumentUrl { get; set; }
    public HostApplicationStatus Status { get; set; } = HostApplicationStatus.Pending;
    public string? RejectionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
