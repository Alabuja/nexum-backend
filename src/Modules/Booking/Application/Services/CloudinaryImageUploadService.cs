using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexum.SharedKernel.Models;

namespace Nexum.Modules.Booking.Application.Services;

public interface IImageUploadService
{
    /// <summary>
    /// Upload up to 3 images for a property.
    /// Returns Cloudinary secure URLs.
    /// </summary>
    Task<ApiResponse<List<string>>> UploadPropertyImagesAsync(
        string propertyId,
        IEnumerable<IFormFile> files,
        CancellationToken ct = default);

    Task<ApiResponse<bool>> DeleteImageAsync(string publicId, CancellationToken ct = default);
}

public sealed class CloudinaryImageUploadService : IImageUploadService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryImageUploadService> _logger;

    private static readonly string[] AllowedTypes =
        ["image/jpeg", "image/jpg", "image/png", "image/webp"];

    private const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB per image
    private const int MaxImages = 3;

    public CloudinaryImageUploadService(IConfiguration config,
        ILogger<CloudinaryImageUploadService> logger)
    {
        _logger = logger;

        var cloudName = config["Cloudinary:CloudName"]
            ?? throw new InvalidOperationException("Cloudinary:CloudName not configured");
        var apiKey = config["Cloudinary:ApiKey"]
            ?? throw new InvalidOperationException("Cloudinary:ApiKey not configured");
        var apiSecret = config["Cloudinary:ApiSecret"]
            ?? throw new InvalidOperationException("Cloudinary:ApiSecret not configured");

        _cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
        _cloudinary.Api.Secure = true; // always use https
    }

    public async Task<ApiResponse<List<string>>> UploadPropertyImagesAsync(
        string propertyId,
        IEnumerable<IFormFile> files,
        CancellationToken ct = default)
    {
        var fileList = files.ToList();

        // ── Validation ────────────────────────────────────
        if (fileList.Count == 0)
            return ApiResponse<List<string>>.Fail("NO_FILES", "No images provided.");

        if (fileList.Count > MaxImages)
            return ApiResponse<List<string>>.Fail("TOO_MANY_FILES",
                $"Maximum {MaxImages} images per property.");

        foreach (var file in fileList)
        {
            if (file.Length == 0)
                return ApiResponse<List<string>>.Fail("EMPTY_FILE",
                    $"File '{file.FileName}' is empty.");

            if (file.Length > MaxFileSizeBytes)
                return ApiResponse<List<string>>.Fail("FILE_TOO_LARGE",
                    $"'{file.FileName}' exceeds the 5 MB limit.");

            if (!AllowedTypes.Contains(file.ContentType.ToLower()))
                return ApiResponse<List<string>>.Fail("INVALID_TYPE",
                    $"'{file.FileName}' must be JPEG, PNG, or WebP.");
        }

        // ── Upload each file to Cloudinary ────────────────
        var urls = new List<string>();

        for (int i = 0; i < fileList.Count; i++)
        {
            var file = fileList[i];
            using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                // Organised under nexum/properties/{propertyId}/
                PublicId = $"nexum/properties/{propertyId}/photo_{i + 1}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Folder = $"nexum/properties/{propertyId}",
                // Auto-optimise: strip metadata, convert to WebP for smaller size
                Transformation = new Transformation()
                    .Width(1200).Height(800).Crop("limit")
                    .FetchFormat("webp").Quality("auto"),
                Overwrite = false,
            };

            try
            {
                var result = await _cloudinary.UploadAsync(uploadParams, ct);

                if (result.Error is not null)
                {
                    _logger.LogError(
                        "Cloudinary upload failed for property {PropertyId}: {Error}",
                        propertyId, result.Error.Message);
                    return ApiResponse<List<string>>.Fail("UPLOAD_FAILED",
                        $"Image {i + 1} upload failed: {result.Error.Message}");
                }

                urls.Add(result.SecureUrl.ToString());

                _logger.LogInformation(
                    "Uploaded image {Index}/{Total} for property {PropertyId}: {Url}",
                    i + 1, fileList.Count, propertyId, result.SecureUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloudinary exception for property {PropertyId}", propertyId);
                return ApiResponse<List<string>>.Fail("UPLOAD_EXCEPTION",
                    "Image upload service unavailable. Please try again.");
            }
        }

        return ApiResponse<List<string>>.Ok(urls);
    }

    public async Task<ApiResponse<bool>> DeleteImageAsync(string publicId, CancellationToken ct = default)
    {
        try
        {
            var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId));
            if (result.Result == "ok")
                return ApiResponse<bool>.Ok(true);
            return ApiResponse<bool>.Fail("DELETE_FAILED", $"Could not delete image: {result.Result}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Cloudinary image {PublicId}", publicId);
            return ApiResponse<bool>.Fail("DELETE_EXCEPTION", "Delete service unavailable.");
        }
    }
}
