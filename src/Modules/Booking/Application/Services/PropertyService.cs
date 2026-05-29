using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Booking.Domain.Enums;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;

namespace Nexum.Modules.Booking.Application.Services;

public interface IPropertyService
{
    Task<ApiResponse<PropertyDto>> CreateAsync(string hostId, CreatePropertyRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<PropertyDto>>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PropertyDto>> GetAsync(Guid propertyId, CancellationToken ct = default);
    Task<ApiResponse<bool>> UpdateAsync(string hostId, Guid propertyId, UpdatePropertyRequest request, CancellationToken ct = default);
    Task<ApiResponse<RoomTypeDto>> AddRoomTypeAsync(string hostId, Guid propertyId, CreateRoomTypeRequest request, CancellationToken ct = default);
    Task<ApiResponse<List<RoomTypeDto>>> GetRoomTypesAsync(Guid propertyId, CancellationToken ct = default);
    Task<ApiResponse<List<AvailabilityDto>>> GetAvailabilityAsync(Guid propertyId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct = default);
    Task<ApiResponse<bool>> SetAvailabilityAsync(string hostId, Guid roomTypeId, CreateRoomAvailabilityRequest request, CancellationToken ct = default);

    // Admin
    Task<ApiResponse<bool>> ApproveAsync(Guid propertyId, CancellationToken ct = default);
    Task<ApiResponse<bool>> RejectAsync(Guid propertyId, string reason, CancellationToken ct = default);
    Task<ApiResponse<bool>> RecordSupervisionAsync(Guid propertyId, DateTime visitDate, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<PropertyDto>>> AdminListAsync(int page, int pageSize, string? status, CancellationToken ct = default);
}

public sealed class PropertyService : IPropertyService
{
    private readonly NexumDbContext _db;
    private readonly ILogger<PropertyService> _logger;

    public PropertyService(NexumDbContext db, ILogger<PropertyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiResponse<PropertyDto>> CreateAsync(string hostId, CreatePropertyRequest request,
        CancellationToken ct = default)
    {
        var property = new Property
        {
            HostId = hostId,
            Name = request.Name,
            Description = request.Description,
            Address = request.Address,
            PhotoUrls = request.PhotoUrls ?? [],
            Status = PropertyStatus.PendingApproval
        };
        _db.Properties.Add(property);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Property {PropertyId} created by host {HostId}", property.Id, hostId);
        return ApiResponse<PropertyDto>.Ok(await MapToDtoAsync(property, ct));
    }

    public async Task<ApiResponse<PagedResult<PropertyDto>>> ListAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Properties
            .Where(p => p.Status == PropertyStatus.Approved)
            .OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = new List<PropertyDto>();
        foreach (var p in items) dtos.Add(await MapToDtoAsync(p, ct));

        return ApiResponse<PagedResult<PropertyDto>>.Ok(new PagedResult<PropertyDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PropertyDto>> GetAsync(Guid propertyId, CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null)
            return ApiResponse<PropertyDto>.Fail("PROPERTY_NOT_FOUND", "Property not found.");
        return ApiResponse<PropertyDto>.Ok(await MapToDtoAsync(property, ct));
    }

    public async Task<ApiResponse<bool>> UpdateAsync(string hostId, Guid propertyId,
        UpdatePropertyRequest request, CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null || property.HostId != hostId)
            return ApiResponse<bool>.Fail("PROPERTY_NOT_FOUND", "Property not found.");

        if (request.Name is not null) property.Name = request.Name;
        if (request.Description is not null) property.Description = request.Description;
        if (request.Address is not null) property.Address = request.Address;
        if (request.PhotoUrls is not null) property.PhotoUrls = request.PhotoUrls;
        property.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<RoomTypeDto>> AddRoomTypeAsync(string hostId, Guid propertyId,
        CreateRoomTypeRequest request, CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null || property.HostId != hostId)
            return ApiResponse<RoomTypeDto>.Fail("PROPERTY_NOT_FOUND", "Property not found.");

        var roomType = new RoomType
        {
            PropertyId = propertyId,
            Name = request.Name,
            Description = request.Description,
            Capacity = request.Capacity,
            PricePerNight = request.PricePerNight,
            Amenities = request.Amenities ?? [],
            PhotoUrls = request.PhotoUrls ?? []
        };
        _db.RoomTypes.Add(roomType);

        // Seed availability for next 365 days
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (int i = 0; i < 365; i++)
        {
            _db.RoomAvailabilities.Add(new RoomAvailability
            {
                RoomTypeId = roomType.Id,
                Date = today.AddDays(i),
                TotalUnits = request.TotalUnits,
                AvailableUnits = request.TotalUnits
            });
        }

        await _db.SaveChangesAsync(ct);
        return ApiResponse<RoomTypeDto>.Ok(MapRoomTypeToDto(roomType));
    }

    public async Task<ApiResponse<List<RoomTypeDto>>> GetRoomTypesAsync(Guid propertyId,
        CancellationToken ct = default)
    {
        var rooms = await _db.RoomTypes
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .ToListAsync(ct);
        return ApiResponse<List<RoomTypeDto>>.Ok(rooms.Select(MapRoomTypeToDto).ToList());
    }

    public async Task<ApiResponse<List<AvailabilityDto>>> GetAvailabilityAsync(Guid propertyId,
        DateOnly checkIn, DateOnly checkOut, CancellationToken ct = default)
    {
        var dates = GetDateRange(checkIn, checkOut);
        var roomTypeIds = await _db.RoomTypes
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var availability = await _db.RoomAvailabilities
            .Where(a => roomTypeIds.Contains(a.RoomTypeId) && dates.Contains(a.Date))
            .Include(a => a.RoomType)
            .ToListAsync(ct);

        // A room is available only if ALL dates in range have units > 0
        var result = roomTypeIds
            .Select(roomTypeId =>
            {
                var roomDates = availability.Where(a => a.RoomTypeId == roomTypeId).ToList();
                var minAvailable = roomDates.Any() ? roomDates.Min(a => a.AvailableUnits) : 0;
                var roomType = roomDates.FirstOrDefault()?.RoomType;
                return new AvailabilityDto(
                    roomTypeId,
                    roomType?.Name ?? "",
                    checkIn,
                    minAvailable,
                    roomType?.PricePerNight ?? 0
                );
            })
            .Where(a => a.AvailableUnits > 0)
            .ToList();

        return ApiResponse<List<AvailabilityDto>>.Ok(result);
    }

    public async Task<ApiResponse<bool>> SetAvailabilityAsync(string hostId, Guid roomTypeId,
        CreateRoomAvailabilityRequest request, CancellationToken ct = default)
    {
        var roomType = await _db.RoomTypes.Include(r => r.Property)
            .FirstOrDefaultAsync(r => r.Id == roomTypeId, ct);
        if (roomType is null || roomType.Property.HostId != hostId)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Room type not found.");

        var dates = GetDateRange(request.From, request.To);
        var existing = await _db.RoomAvailabilities
            .Where(a => a.RoomTypeId == roomTypeId && dates.Contains(a.Date))
            .ToListAsync(ct);

        foreach (var date in dates)
        {
            var avail = existing.FirstOrDefault(a => a.Date == date);
            if (avail is null)
            {
                _db.RoomAvailabilities.Add(new RoomAvailability
                {
                    RoomTypeId = roomTypeId, Date = date,
                    TotalUnits = request.Units, AvailableUnits = request.Units
                });
            }
            else
            {
                avail.TotalUnits = request.Units;
                avail.AvailableUnits = request.Units;
                avail.UpdatedAt = DateTime.UtcNow;
            }
        }
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> ApproveAsync(Guid propertyId, CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null) return ApiResponse<bool>.Fail("PROPERTY_NOT_FOUND", "Not found.");
        property.Status = PropertyStatus.Approved;
        property.LastSupervisedAt = DateTime.UtcNow;
        property.NextSupervisionDue = DateTime.UtcNow.AddMonths(18);
        property.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> RejectAsync(Guid propertyId, string reason,
        CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null) return ApiResponse<bool>.Fail("PROPERTY_NOT_FOUND", "Not found.");
        property.Status = PropertyStatus.Rejected;
        property.RejectionReason = reason;
        property.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> RecordSupervisionAsync(Guid propertyId, DateTime visitDate,
        CancellationToken ct = default)
    {
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null) return ApiResponse<bool>.Fail("PROPERTY_NOT_FOUND", "Not found.");
        property.LastSupervisedAt = visitDate;
        property.NextSupervisionDue = visitDate.AddMonths(18);
        property.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<PagedResult<PropertyDto>>> AdminListAsync(int page, int pageSize,
        string? status, CancellationToken ct = default)
    {
        var query = _db.Properties.AsQueryable();
        if (status is not null && Enum.TryParse<PropertyStatus>(status, true, out var s))
            query = query.Where(p => p.Status == s);
        query = query.OrderByDescending(p => p.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = new List<PropertyDto>();
        foreach (var p in items) dtos.Add(await MapToDtoAsync(p, ct));

        return ApiResponse<PagedResult<PropertyDto>>.Ok(new PagedResult<PropertyDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    // ── Helpers ──────────────────────────────────────────────
    private async Task<PropertyDto> MapToDtoAsync(Property p, CancellationToken ct)
    {
        var host = await _db.Users.FindAsync([p.HostId], ct);
        return new PropertyDto(p.Id, p.HostId, host?.FullName ?? "", p.Name, p.Description,
            p.Address, p.PhotoUrls, p.Status.ToString(), p.LastSupervisedAt,
            p.NextSupervisionDue, p.CreatedAt);
    }

    private static RoomTypeDto MapRoomTypeToDto(RoomType r) =>
        new(r.Id, r.PropertyId, r.Name, r.Description, r.Capacity,
            r.PricePerNight, r.Amenities, r.PhotoUrls, r.IsActive);

    private static List<DateOnly> GetDateRange(DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        for (var d = from; d < to; d = d.AddDays(1)) dates.Add(d);
        return dates;
    }
}
