using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Booking.Application.DTOs;

// ── Properties ────────────────────────────────────────────────
public sealed record CreatePropertyRequest(
    [Required] string Name,
    [Required] string Description,
    [Required] string Address,
    List<string>? PhotoUrls
);

public sealed record UpdatePropertyRequest(
    string? Name,
    string? Description,
    string? Address,
    List<string>? PhotoUrls
);

public sealed record PropertyDto(
    Guid Id,
    string HostId,
    string HostName,
    string Name,
    string Description,
    string Address,
    List<string> PhotoUrls,
    string Status,
    DateTime? LastSupervisedAt,
    DateTime? NextSupervisionDue,
    DateTime CreatedAt
);

// ── Room Types ────────────────────────────────────────────────
public sealed record CreateRoomTypeRequest(
    [Required] string Name,
    [Required] string Description,
    [Required, Range(1, 20)] int Capacity,
    [Required, Range(1, 9999999)] decimal PricePerNight,
    List<string>? Amenities,
    List<string>? PhotoUrls,
    [Required, Range(1, 100)] int TotalUnits
);

public sealed record RoomTypeDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string Description,
    int Capacity,
    decimal PricePerNight,
    List<string> Amenities,
    List<string> PhotoUrls,
    bool IsActive
);

public sealed record AvailabilityDto(
    Guid RoomTypeId,
    string RoomTypeName,
    DateOnly Date,
    int AvailableUnits,
    decimal PricePerNight
);

// ── Bookings ──────────────────────────────────────────────────
public sealed record CreateBookingRequest(
    [Required] Guid PropertyId,
    [Required] Guid RoomTypeId,
    [Required] DateOnly CheckInDate,
    [Required] DateOnly CheckOutDate
);

public sealed record BookingDto(
    Guid Id,
    string GuestId,
    string? GuestName,
    Guid PropertyId,
    string? PropertyName,
    Guid RoomTypeId,
    string? RoomTypeName,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int NumNights,
    decimal TotalAmount,
    string Status,
    string? ConfirmationCode,
    string? PaystackReference,
    DateTime? PaidAt,
    DateTime CreatedAt
);

public sealed record CreateBookingResponse(
    Guid BookingId,
    string PaystackAuthorizationUrl,
    string PaystackReference,
    decimal TotalAmount
);

// ── Paystack ──────────────────────────────────────────────────
public sealed record PaystackWebhookPayload(
    string Event,
    PaystackWebhookData Data
);

public sealed record PaystackWebhookData(
    string Reference,
    string Status,
    long Amount,
    PaystackCustomer Customer
);

public sealed record PaystackCustomer(
    string Email
);

// ── Admin ─────────────────────────────────────────────────────
public sealed record ApprovePropertyRequest();
public sealed record RejectPropertyRequest([Required] string Reason);
public sealed record RecordSupervisionRequest([Required] DateTime VisitDate);
public sealed record ApproveHostRequest();
public sealed record RejectHostRequest([Required] string Reason);
public sealed record CreateRoomAvailabilityRequest(
    [Required] DateOnly From,
    [Required] DateOnly To,
    [Required, Range(1, 100)] int Units
);

// ── Host application ──────────────────────────────────────────
public sealed record HostApplicationRequest(
    [Required] string BusinessName,
    [Required] string PhoneNumber,
    string? IdentityDocumentUrl
);

public sealed record HostApplicationDto(
    Guid Id,
    string UserId,
    string? UserName,
    string BusinessName,
    string PhoneNumber,
    string? IdentityDocumentUrl,
    string Status,
    string? RejectionReason,
    DateTime CreatedAt
);
