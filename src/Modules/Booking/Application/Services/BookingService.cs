using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Booking.Domain.Enums;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;

namespace Nexum.Modules.Booking.Application.Services;

public interface IBookingService
{
    Task<ApiResponse<CreateBookingResponse>> CreateAsync(string guestId, string guestEmail, CreateBookingRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<BookingDto>>> GetMyBookingsAsync(string guestId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<BookingDto>> GetAsync(string userId, Guid bookingId, CancellationToken ct = default);
    Task<ApiResponse<BookingDto>> LookupByCodeAsync(string confirmationCode, CancellationToken ct = default);
    Task<ApiResponse<bool>> CheckInAsync(string hostId, Guid bookingId, CancellationToken ct = default);
    Task<ApiResponse<bool>> CancelAsync(string userId, Guid bookingId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<BookingDto>>> AdminListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<bool>> HandlePaystackWebhookAsync(string payload, string signature, CancellationToken ct = default);
}

public sealed class BookingService : IBookingService
{
    private readonly NexumDbContext _db;
    private readonly BookingLockRegistry _lockRegistry;
    private readonly IPaystackService _paystack;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        NexumDbContext db,
        BookingLockRegistry lockRegistry,
        IPaystackService paystack,
        IEmailService email,
        IConfiguration config,
        ILogger<BookingService> logger)
    {
        _db = db;
        _lockRegistry = lockRegistry;
        _paystack = paystack;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public async Task<ApiResponse<CreateBookingResponse>> CreateAsync(
        string guestId, string guestEmail,
        CreateBookingRequest request, CancellationToken ct = default)
    {
        // Validate dates
        if (request.CheckInDate >= request.CheckOutDate)
            return ApiResponse<CreateBookingResponse>.Fail("INVALID_DATES",
                "Check-out must be after check-in.");

        if (request.CheckInDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return ApiResponse<CreateBookingResponse>.Fail("INVALID_DATES",
                "Check-in date cannot be in the past.");

        var dates = GetDateRange(request.CheckInDate, request.CheckOutDate);
        var numNights = dates.Count;

        // ── Pessimistic code-level lock ──────────────────────
        // WaitAsync(0) = fail immediately if another request holds the lock.
        // This prevents two simultaneous bookings of the same room+dates
        // from both passing the availability check.
        var semaphore = _lockRegistry.GetLock(request.RoomTypeId, dates);
        bool acquired = await semaphore.WaitAsync(0, ct);

        if (!acquired)
        {
            // Another request is currently booking this room for these dates.
            // Return immediately — no waiting, no retry.
            return ApiResponse<CreateBookingResponse>.Fail("ROOM_UNAVAILABLE",
                "This room is currently being booked by another guest. Please try again.");
        }

        try
        {
            // ── Availability check (inside lock — safe read) ──
            var roomType = await _db.RoomTypes
                .Include(r => r.Property)
                .FirstOrDefaultAsync(r => r.Id == request.RoomTypeId, ct);

            if (roomType is null || !roomType.IsActive)
                return ApiResponse<CreateBookingResponse>.Fail("ROOM_NOT_FOUND", "Room type not found.");

            if (roomType.Property.Status != PropertyStatus.Approved)
                return ApiResponse<CreateBookingResponse>.Fail("PROPERTY_NOT_APPROVED",
                    "This property is not accepting bookings.");

            var availabilityRows = await _db.RoomAvailabilities
                .Where(a => a.RoomTypeId == request.RoomTypeId && dates.Contains(a.Date))
                .ToListAsync(ct);

            // Must have a row for every date in range
            if (availabilityRows.Count < numNights)
                return ApiResponse<CreateBookingResponse>.Fail("ROOM_UNAVAILABLE",
                    "Room is not available for some dates in the selected range.");

            // All dates must have at least 1 unit available
            if (availabilityRows.Any(a => a.AvailableUnits < 1))
                return ApiResponse<CreateBookingResponse>.Fail("ROOM_UNAVAILABLE",
                    "Room is fully booked for the selected dates.");

            // ── Decrement availability ────────────────────────
            foreach (var row in availabilityRows)
            {
                row.AvailableUnits -= 1;
                row.UpdatedAt = DateTime.UtcNow;
            }

            // ── Create booking (status = PendingPayment) ──────
            var totalAmount = roomType.PricePerNight * numNights;
            var paystackRef = $"NEXUM-{Guid.NewGuid():N}".ToUpper()[..20];

            var booking = new BookingEntity
            {
                GuestId = guestId,
                PropertyId = roomType.PropertyId,
                RoomTypeId = request.RoomTypeId,
                CheckInDate = request.CheckInDate,
                CheckOutDate = request.CheckOutDate,
                NumNights = numNights,
                TotalAmount = totalAmount,
                Status = BookingStatus.PendingPayment,
                PaystackReference = paystackRef
            };
            _db.Bookings.Add(booking);

            // Save availability decrement + booking atomically
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Booking {BookingId} created for guest {GuestId} — pending payment",
                booking.Id, guestId);

            // ── Initialize Paystack transaction ───────────────
            var callbackUrl = _config["Paystack:CallbackUrl"]
                ?? "http://localhost:3000/worshipper/bookings";

            var paystackResult = await _paystack.InitializeTransactionAsync(
                guestEmail, totalAmount, paystackRef, callbackUrl, ct);

            if (!paystackResult.Success)
            {
                // Paystack init failed — release availability and cancel booking
                foreach (var row in availabilityRows) row.AvailableUnits += 1;
                booking.Status = BookingStatus.Cancelled;
                await _db.SaveChangesAsync(ct);
                return ApiResponse<CreateBookingResponse>.Fail("PAYMENT_INIT_FAILED",
                    paystackResult.Message ?? "Could not initialise payment.");
            }

            return ApiResponse<CreateBookingResponse>.Ok(new CreateBookingResponse(
                booking.Id,
                paystackResult.AuthorizationUrl!,
                paystackRef,
                totalAmount));
        }
        finally
        {
            // ALWAYS release — whether success, failure, or exception.
            // A leaked semaphore would permanently block all future bookings
            // for this room+date combination.
            semaphore.Release();
        }
    }

    public async Task<ApiResponse<bool>> HandlePaystackWebhookAsync(
        string payload, string signature, CancellationToken ct = default)
    {
        // Verify HMAC-SHA512 signature from Paystack
        if (!_paystack.VerifyWebhookSignature(payload, signature))
        {
            _logger.LogWarning("Paystack webhook signature verification failed");
            return ApiResponse<bool>.Fail("INVALID_SIGNATURE", "Webhook signature mismatch.");
        }

        PaystackWebhookPayload? webhookData;
        try
        {
            webhookData = System.Text.Json.JsonSerializer.Deserialize<PaystackWebhookPayload>(
                payload,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize Paystack webhook");
            return ApiResponse<bool>.Fail("INVALID_PAYLOAD", "Could not parse webhook payload.");
        }

        if (webhookData?.Event != "charge.success")
        {
            // Not a payment success event — acknowledge and ignore
            return ApiResponse<bool>.Ok(true);
        }

        var reference = webhookData.Data.Reference;
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.PaystackReference == reference, ct);

        if (booking is null)
        {
            _logger.LogWarning("Paystack webhook: no booking found for reference {Ref}", reference);
            return ApiResponse<bool>.Ok(true); // Acknowledge to Paystack
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            // Already processed — idempotent
            return ApiResponse<bool>.Ok(true);
        }

        // ── Generate 8-char confirmation code ─────────────────
        string code;
        int attempts = 0;
        do
        {
            code = ConfirmationCodeGenerator.Generate();
            attempts++;
            if (attempts > 10) break; // Safety valve
        }
        while (await _db.Bookings.AnyAsync(b => b.ConfirmationCode == code, ct));

        booking.Status = BookingStatus.Confirmed;
        booking.ConfirmationCode = code;
        booking.PaidAt = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Booking {BookingId} confirmed via Paystack. Code: {Code}",
            booking.Id, code);

        // ── Send confirmation email ────────────────────────────
        var guest = await _db.Users.FindAsync([booking.GuestId], ct);
        if (guest?.Email is not null)
        {
            var property = await _db.Properties.FindAsync([booking.PropertyId], ct);
            await _email.SendAsync(
                guest.Email, guest.FullName,
                "Nexum — Booking Confirmed 🎉",
                $"""
                <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto">
                  <h2 style="color:#1B3A6B">Booking Confirmed!</h2>
                  <p>Your booking at <strong>{property?.Name ?? "the property"}</strong> is confirmed.</p>
                  <div style="background:#EFF6FF;border-radius:12px;padding:20px;margin:20px 0;text-align:center">
                    <div style="font-size:12px;color:#6B7280;margin-bottom:8px">Check-in confirmation code</div>
                    <div style="font-family:monospace;font-size:32px;font-weight:bold;color:#1B3A6B;letter-spacing:6px">{code}</div>
                    <div style="font-size:12px;color:#6B7280;margin-top:8px">Show this code to the host at check-in</div>
                  </div>
                  <table style="width:100%;border-collapse:collapse">
                    <tr><td style="padding:8px 0;color:#6B7280">Check-in</td><td style="padding:8px 0;font-weight:bold">{booking.CheckInDate:dd MMM yyyy}</td></tr>
                    <tr><td style="padding:8px 0;color:#6B7280">Check-out</td><td style="padding:8px 0;font-weight:bold">{booking.CheckOutDate:dd MMM yyyy}</td></tr>
                    <tr><td style="padding:8px 0;color:#6B7280">Total paid</td><td style="padding:8px 0;font-weight:bold">₦{booking.TotalAmount:N0}</td></tr>
                  </table>
                </div>
                """,
                ct);
        }

        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<PagedResult<BookingDto>>> GetMyBookingsAsync(
        string guestId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Bookings
            .Where(b => b.GuestId == guestId)
            .OrderByDescending(b => b.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = new List<BookingDto>();
        foreach (var b in items) dtos.Add(await MapToDtoAsync(b, ct));

        return ApiResponse<PagedResult<BookingDto>>.Ok(new PagedResult<BookingDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<BookingDto>> GetAsync(string userId, Guid bookingId,
        CancellationToken ct = default)
    {
        var booking = await _db.Bookings.FindAsync([bookingId], ct);
        if (booking is null || booking.GuestId != userId)
            return ApiResponse<BookingDto>.Fail("BOOKING_NOT_FOUND", "Booking not found.");
        return ApiResponse<BookingDto>.Ok(await MapToDtoAsync(booking, ct));
    }

    public async Task<ApiResponse<BookingDto>> LookupByCodeAsync(string confirmationCode,
        CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .FirstOrDefaultAsync(b => b.ConfirmationCode == confirmationCode.ToUpper(), ct);
        if (booking is null)
            return ApiResponse<BookingDto>.Fail("CONFIRMATION_CODE_INVALID",
                "Confirmation code not found.");
        return ApiResponse<BookingDto>.Ok(await MapToDtoAsync(booking, ct));
    }

    public async Task<ApiResponse<bool>> CheckInAsync(string hostId, Guid bookingId,
        CancellationToken ct = default)
    {
        var booking = await _db.Bookings
            .Include(b => b.Property)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking is null)
            return ApiResponse<bool>.Fail("BOOKING_NOT_FOUND", "Booking not found.");
        if (booking.Property.HostId != hostId)
            return ApiResponse<bool>.Fail("FORBIDDEN", "Not your property.");
        if (booking.Status != BookingStatus.Confirmed)
            return ApiResponse<bool>.Fail("BOOKING_NOT_CONFIRMED",
                "Booking must be confirmed before check-in.");

        booking.Status = BookingStatus.CheckedIn;
        booking.CheckedInAt = DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> CancelAsync(string userId, Guid bookingId,
        CancellationToken ct = default)
    {
        var booking = await _db.Bookings.FindAsync([bookingId], ct);
        if (booking is null || booking.GuestId != userId)
            return ApiResponse<bool>.Fail("BOOKING_NOT_FOUND", "Booking not found.");
        if (booking.Status is BookingStatus.CheckedIn or BookingStatus.Completed)
            return ApiResponse<bool>.Fail("CANNOT_CANCEL", "Cannot cancel a completed booking.");

        booking.Status = BookingStatus.Cancelled;
        booking.UpdatedAt = DateTime.UtcNow;

        // Release availability
        var dates = GetDateRange(booking.CheckInDate, booking.CheckOutDate);
        var rows = await _db.RoomAvailabilities
            .Where(a => a.RoomTypeId == booking.RoomTypeId && dates.Contains(a.Date))
            .ToListAsync(ct);
        foreach (var row in rows) row.AvailableUnits += 1;

        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<PagedResult<BookingDto>>> AdminListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Bookings.OrderByDescending(b => b.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = new List<BookingDto>();
        foreach (var b in items) dtos.Add(await MapToDtoAsync(b, ct));
        return ApiResponse<PagedResult<BookingDto>>.Ok(new PagedResult<BookingDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    // ── Helpers ──────────────────────────────────────────────
    private async Task<BookingDto> MapToDtoAsync(BookingEntity b, CancellationToken ct)
    {
        var guest    = await _db.Users.FindAsync([b.GuestId], ct);
        var property = await _db.Properties.FindAsync([b.PropertyId], ct);
        var room     = await _db.RoomTypes.FindAsync([b.RoomTypeId], ct);
        return new BookingDto(b.Id, b.GuestId, guest?.FullName, b.PropertyId,
            property?.Name, b.RoomTypeId, room?.Name, b.CheckInDate, b.CheckOutDate,
            b.NumNights, b.TotalAmount, b.Status.ToString(), b.ConfirmationCode,
            b.PaystackReference, b.PaidAt, b.CreatedAt);
    }

    /// <summary>
    /// Called by Hangfire every 5 minutes.
    /// Finds PendingPayment bookings older than 15 minutes,
    /// releases their availability, and marks them Expired.
    /// </summary>
    public async Task ExpireOldBookingsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        var expired = await _db.Bookings
            .Where(b => b.Status == BookingStatus.PendingPayment && b.CreatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var booking in expired)
        {
            booking.Status = BookingStatus.Expired;
            booking.UpdatedAt = DateTime.UtcNow;

            // Release availability
            var dates = GetDateRange(booking.CheckInDate, booking.CheckOutDate);
            var rows = await _db.RoomAvailabilities
                .Where(a => a.RoomTypeId == booking.RoomTypeId && dates.Contains(a.Date))
                .ToListAsync(ct);
            foreach (var row in rows) row.AvailableUnits += 1;

            _logger.LogInformation("Booking {BookingId} expired — availability released", booking.Id);
        }

        if (expired.Any())
            await _db.SaveChangesAsync(ct);
    }

    private static List<DateOnly> GetDateRange(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d < to; d = d.AddDays(1)) dates.Add(d);
        return dates;
    }
}
