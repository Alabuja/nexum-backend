using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Transit.Domain.Entities;
using Nexum.SharedKernel.Models;

namespace Nexum.Modules.Transit.Application.Services;

// ── DTOs ──────────────────────────────────────────────────────
public sealed record SubmitVehicleRequest(string Registration, int Capacity, string? VehicleType);
public sealed record AdminCreateVehicleRequest(string DriverId, string Registration, int Capacity, string? VehicleType);
public sealed record VehicleDto(
    Guid Id,
    string DriverId,
    string? DriverName,
    string Registration,
    int Capacity,
    string? VehicleType,
    string Status,
    string? RejectionReason,
    DateTime CreatedAt
);

// ── Interface ─────────────────────────────────────────────────
public interface IVehicleService
{
    Task<ApiResponse<VehicleDto?>> GetMyVehicleAsync(string driverId, CancellationToken ct = default);
    Task<ApiResponse<VehicleDto>> SubmitVehicleAsync(string driverId, SubmitVehicleRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<VehicleDto>>> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<bool>> ApproveAsync(Guid vehicleId, CancellationToken ct = default);
    Task<ApiResponse<bool>> RejectAsync(Guid vehicleId, string reason, CancellationToken ct = default);
    Task<ApiResponse<VehicleDto>> AdminCreateAsync(AdminCreateVehicleRequest request, CancellationToken ct = default);
}

// ── Implementation ────────────────────────────────────────────
public sealed class VehicleService : IVehicleService
{
    private readonly NexumDbContext _db;
    private readonly ILogger<VehicleService> _logger;

    public VehicleService(NexumDbContext db, ILogger<VehicleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ApiResponse<VehicleDto?>> GetMyVehicleAsync(
        string driverId, CancellationToken ct = default)
    {
        var vehicle = await _db.ShuttleVehicles
            .FirstOrDefaultAsync(v => v.DriverId == driverId, ct);

        if (vehicle is null)
            return ApiResponse<VehicleDto?>.Ok(null);

        var driver = await _db.Users.FindAsync([driverId], ct);
        return ApiResponse<VehicleDto?>.Ok(MapToDto(vehicle, driver?.FullName));
    }

    public async Task<ApiResponse<VehicleDto>> SubmitVehicleAsync(
        string driverId, SubmitVehicleRequest request, CancellationToken ct = default)
    {
        // Only one vehicle per driver
        var existing = await _db.ShuttleVehicles
            .FirstOrDefaultAsync(v => v.DriverId == driverId, ct);
        if (existing is not null)
            return ApiResponse<VehicleDto>.Fail("VEHICLE_EXISTS",
                "You already have a vehicle registered. Contact admin to change it.");

        // Registration must be unique
        var regExists = await _db.ShuttleVehicles
            .AnyAsync(v => v.Registration == request.Registration.ToUpper(), ct);
        if (regExists)
            return ApiResponse<VehicleDto>.Fail("REGISTRATION_TAKEN",
                "This registration is already registered to another driver.");

        var vehicle = new ShuttleVehicle
        {
            Id           = Guid.NewGuid(),
            DriverId     = driverId,
            Registration = request.Registration.ToUpper().Trim(),
            Capacity     = request.Capacity,
            VehicleType  = request.VehicleType ?? "Bus",
            Status       = ShuttleStatus.PendingApproval,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
        };
        _db.ShuttleVehicles.Add(vehicle);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Vehicle {Reg} submitted by driver {DriverId}", vehicle.Registration, driverId);
        var driver = await _db.Users.FindAsync([driverId], ct);
        return ApiResponse<VehicleDto>.Ok(MapToDto(vehicle, driver?.FullName));
    }

    public async Task<ApiResponse<PagedResult<VehicleDto>>> ListAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ShuttleVehicles.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ShuttleStatus>(status, true, out var parsedStatus))
            query = query.Where(v => v.Status == parsedStatus);

        query = query.OrderByDescending(v => v.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var dtos = new List<VehicleDto>();
        foreach (var v in items)
        {
            var driver = await _db.Users.FindAsync([v.DriverId], ct);
            dtos.Add(MapToDto(v, driver?.FullName));
        }

        return ApiResponse<PagedResult<VehicleDto>>.Ok(new PagedResult<VehicleDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<bool>> ApproveAsync(Guid vehicleId, CancellationToken ct = default)
    {
        var vehicle = await _db.ShuttleVehicles.FindAsync([vehicleId], ct);
        if (vehicle is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Vehicle not found.");

        vehicle.Status          = ShuttleStatus.Available;
        vehicle.RejectionReason = null;
        vehicle.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Vehicle {VehicleId} approved", vehicleId);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> RejectAsync(
        Guid vehicleId, string reason, CancellationToken ct = default)
    {
        var vehicle = await _db.ShuttleVehicles.FindAsync([vehicleId], ct);
        if (vehicle is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Vehicle not found.");

        vehicle.Status          = ShuttleStatus.Rejected;
        vehicle.RejectionReason = reason;
        vehicle.UpdatedAt       = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Vehicle {VehicleId} rejected: {Reason}", vehicleId, reason);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<VehicleDto>> AdminCreateAsync(
        AdminCreateVehicleRequest request, CancellationToken ct = default)
    {
        var vehicle = new ShuttleVehicle
        {
            Id           = Guid.NewGuid(),
            DriverId     = request.DriverId,
            Registration = request.Registration.ToUpper().Trim(),
            Capacity     = request.Capacity,
            VehicleType  = request.VehicleType ?? "Bus",
            Status       = ShuttleStatus.Available, // admin-created = auto approved
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
        };
        _db.ShuttleVehicles.Add(vehicle);
        await _db.SaveChangesAsync(ct);

        var driver = await _db.Users.FindAsync([request.DriverId], ct);
        return ApiResponse<VehicleDto>.Ok(MapToDto(vehicle, driver?.FullName));
    }

    private static VehicleDto MapToDto(ShuttleVehicle v, string? driverName) =>
        new(v.Id, v.DriverId, driverName, v.Registration, v.Capacity,
            v.VehicleType, v.Status.ToString(), v.RejectionReason, v.CreatedAt);
}
