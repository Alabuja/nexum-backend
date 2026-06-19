using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Parking.Domain.Entities;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Parking.Application;

// ?? DTOs ??????????????????????????????????????????????????????
public sealed record CreatePinRequest(
	[Required] double Latitude,
	[Required] double Longitude,
	string? AreaLabel,
	string? VehicleDescription,
	string? LicencePlate,
	string? PhotoUrl
);

public sealed record PinDto(
	Guid Id, string UserId, double Latitude, double Longitude,
	string? AreaLabel, string? VehicleDescription, string? LicencePlate,
	string? PhotoUrl, bool IsActive, DateTime CreatedAt
);

public sealed record CreateBlockingAlertRequest(
	[Required] double Latitude,
	[Required] double Longitude,
	string? Note,
	string? LicencePlate = null,
	string? VehicleDescription = null
);

public sealed record BlockingAlertDto(
	Guid Id, string ReporterId, Guid? BlockerPinId,
	string Status, DateTime CreatedAt
);

public interface IParkingService
{
	Task<ApiResponse<PinDto>> CreatePinAsync(string userId, CreatePinRequest request, CancellationToken ct = default);
	Task<ApiResponse<PinDto?>> GetMyPinAsync(string userId, CancellationToken ct = default);
	Task<ApiResponse<bool>> DeactivatePinAsync(string userId, Guid pinId, CancellationToken ct = default);
	Task<ApiResponse<BlockingAlertDto>> CreateBlockingAlertAsync(string userId, CreateBlockingAlertRequest request, CancellationToken ct = default);
}

public sealed class ParkingService : IParkingService
{
	private readonly NexumDbContext _db;
	private readonly INotificationService _notifications;
	private readonly ILogger<ParkingService> _logger;

	// 15 metres proximity for blocker matching
	private const double ProximityMetres = 15.0;

	public ParkingService(NexumDbContext db, INotificationService notifications, ILogger<ParkingService> logger)
	{
		_db = db;
		_notifications = notifications;
		_logger = logger;
	}

	public async Task<ApiResponse<PinDto>> CreatePinAsync(string userId, CreatePinRequest request,
		CancellationToken ct = default)
	{
		var existing = await _db.ParkingPins
			.FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);
		if (existing is not null)
			return ApiResponse<PinDto>.Fail("PIN_ALREADY_ACTIVE",
				"You already have an active parking pin. Remove it first.");

		var pin = new ParkingPin
		{
			UserId = userId,
			PinLocation = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
			AreaLabel = request.AreaLabel,
			VehicleDescription = request.VehicleDescription,
			LicencePlate = request.LicencePlate,
			PhotoUrl = request.PhotoUrl
		};
		_db.ParkingPins.Add(pin);
		await _db.SaveChangesAsync(ct);

		_logger.LogInformation("Parking pin {PinId} created for user {UserId}", pin.Id, userId);
		return ApiResponse<PinDto>.Ok(MapToDto(pin));
	}

	public async Task<ApiResponse<PinDto?>> GetMyPinAsync(string userId, CancellationToken ct = default)
	{
		var pin = await _db.ParkingPins
			.FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);
		return ApiResponse<PinDto?>.Ok(pin is null ? null : MapToDto(pin));
	}

	public async Task<ApiResponse<bool>> DeactivatePinAsync(string userId, Guid pinId,
		CancellationToken ct = default)
	{
		var pin = await _db.ParkingPins.FindAsync([pinId], ct);
		if (pin is null || pin.UserId != userId)
			return ApiResponse<bool>.Fail("PIN_NOT_FOUND", "Parking pin not found.");
		if (!pin.IsActive)
			return ApiResponse<bool>.Fail("PIN_ALREADY_INACTIVE", "Pin is already inactive.");

		pin.IsActive = false;
		pin.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ApiResponse<bool>.Ok(true);
	}

	public async Task<ApiResponse<BlockingAlertDto>> CreateBlockingAlertAsync(string userId,
		CreateBlockingAlertRequest request, CancellationToken ct = default)
	{
		var reporterLocation = new Point(request.Longitude, request.Latitude) { SRID = 4326 };

		// Find reporter's active pin
		var reporterPin = await _db.ParkingPins
			.FirstOrDefaultAsync(p => p.UserId == userId && p.IsActive, ct);

		var candidatePins = await _db.ParkingPins
			.Where(p => p.IsActive && p.UserId != userId)
			.ToListAsync(ct);

		var requestedPlate = NormalizePlate(request.LicencePlate);
		var blockerPin = requestedPlate is not null
			? candidatePins.FirstOrDefault(p => NormalizePlate(p.LicencePlate) == requestedPlate)
			: null;

		blockerPin ??= candidatePins
			.Where(p => p.PinLocation.Distance(reporterLocation) * 111139 <= ProximityMetres)
			.OrderBy(p => p.PinLocation.Distance(reporterLocation))
			.FirstOrDefault();

		var alert = new BlockingAlert
		{
			ReporterId = userId,
			BlockerPinId = blockerPin?.Id,
			ReporterPinId = reporterPin?.Id,
			ReporterLocation = reporterLocation,
			Note = BuildBlockingNote(request),
			Status = blockerPin is null ? BlockingStatus.Escalated : BlockingStatus.Pending
		};
		_db.BlockingAlerts.Add(alert);
		await _db.SaveChangesAsync(ct);

		if (blockerPin is not null)
		{
			// Notify blocking vehicle owner
			var owner = await _db.Users.FindAsync([blockerPin.UserId], ct);
			if (owner?.FcmToken is not null)
			{
				alert.NotifiedAt = DateTime.UtcNow;
				alert.Status = BlockingStatus.Notified;
				await _db.SaveChangesAsync(ct);

				await _notifications.SendPushAsync(owner.FcmToken,
					"?? Your vehicle is blocking another",
					"Please move your vehicle immediately.",
					new Dictionary<string, string>
					{
						["blockingAlertId"] = alert.Id.ToString(),
						["type"] = "blocking_vehicle"
					}, ct);
			}
		}
		else
		{
			// No pin found — escalate to security immediately
			_logger.LogWarning("No blocker pin found for alert {AlertId} — escalating", alert.Id);
		}

		return ApiResponse<BlockingAlertDto>.Ok(
			new BlockingAlertDto(alert.Id, userId, alert.BlockerPinId,
				alert.Status.ToString(), alert.CreatedAt));
	}

	private static PinDto MapToDto(ParkingPin p) =>
		new(p.Id, p.UserId, p.PinLocation.Coordinate.Y, p.PinLocation.Coordinate.X,
			p.AreaLabel, p.VehicleDescription, p.LicencePlate, p.PhotoUrl, p.IsActive, p.CreatedAt);

	private static string? NormalizePlate(string? plate)
	{
		if (string.IsNullOrWhiteSpace(plate)) return null;
		var chars = plate.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray();
		return chars.Length == 0 ? null : new string(chars);
	}

	private static string? BuildBlockingNote(CreateBlockingAlertRequest request)
	{
		var parts = new[]
		{
			string.IsNullOrWhiteSpace(request.LicencePlate) ? null : $"Plate: {request.LicencePlate.Trim().ToUpperInvariant()}",
			string.IsNullOrWhiteSpace(request.VehicleDescription) ? null : $"Vehicle: {request.VehicleDescription.Trim()}",
			string.IsNullOrWhiteSpace(request.Note) ? null : $"Note: {request.Note.Trim()}",
		}.Where(p => p is not null);

		return string.Join(" | ", parts);
	}
}