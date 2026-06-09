using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Application.Services;
using System.Security.Claims;

namespace Nexum.Modules.Booking.Api.Controllers;

[ApiController]
[Route("v1/properties")]
public sealed class PropertyImageController : ControllerBase
{
    private readonly IImageUploadService _uploadService;
    private readonly NexumDbContext _db;

    public PropertyImageController(IImageUploadService uploadService, NexumDbContext db)
    {
        _uploadService = uploadService;
        _db = db;
    }

    /// <summary>
    /// Upload up to 3 photos for a property.
    /// Accepts multipart/form-data with field name "images".
    /// Uploads to Cloudinary, saves returned URLs to the property's PhotoUrls array.
    /// 
    /// POST /v1/properties/{propertyId}/images
    /// Content-Type: multipart/form-data
    /// Body: images (file[]) — max 3 files, max 5MB each, JPEG/PNG/WebP
    /// </summary>
    [HttpPost("{propertyId:guid}/images")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB total for 3 images
    public async Task<IActionResult> UploadImages(
        Guid propertyId,
        [FromForm] List<IFormFile> images,
        CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Verify host owns this property
        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null)
            return NotFound(new { success = false, error = new { code = "NOT_FOUND", message = "Property not found." } });

        if (property.HostId != hostId && !User.IsInRole(Roles.Admin))
            return Forbid();

        // Upload to Cloudinary
        var uploadResult = await _uploadService.UploadPropertyImagesAsync(
            propertyId.ToString(), images, ct);

        if (!uploadResult.Success)
            return BadRequest(uploadResult);

        // Append new URLs to property (replace all photos with new set)
        property.PhotoUrls = uploadResult.Data!;
        property.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(uploadResult);
    }

    /// <summary>
    /// Replace all photos for a property with a new set.
    /// Used when editing — send all 3 (or fewer) photos in one request.
    /// Existing Cloudinary images are NOT automatically deleted (to avoid
    /// deleting images that might still be in use). Handle cleanup separately.
    /// </summary>
    [HttpPut("{propertyId:guid}/images")]
    [Authorize(Roles = $"{Roles.Host},{Roles.Admin}")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceImages(
        Guid propertyId,
        [FromForm] List<IFormFile> images,
        CancellationToken ct)
    {
        var hostId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var property = await _db.Properties.FindAsync([propertyId], ct);
        if (property is null)
            return NotFound(new { success = false, error = new { code = "NOT_FOUND" } });

        if (property.HostId != hostId && !User.IsInRole(Roles.Admin))
            return Forbid();

        var uploadResult = await _uploadService.UploadPropertyImagesAsync(
            propertyId.ToString(), images, ct);

        if (!uploadResult.Success)
            return BadRequest(uploadResult);

        // Replace all photos
        property.PhotoUrls = uploadResult.Data!;
        property.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(uploadResult);
    }
}
