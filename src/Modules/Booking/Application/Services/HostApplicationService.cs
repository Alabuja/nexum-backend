using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Auth.Domain.Enums;
using Nexum.Modules.Booking.Application.DTOs;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Booking.Domain.Enums;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using Microsoft.AspNetCore.Identity;
using Nexum.Modules.Auth.Domain.Entities;

namespace Nexum.Modules.Booking.Application.Services;

public interface IHostApplicationService
{
    Task<ApiResponse<HostApplicationDto>> ApplyAsync(string userId, HostApplicationRequest request, CancellationToken ct = default);
    Task<ApiResponse<HostApplicationDto?>> GetMyApplicationAsync(string userId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<HostApplicationDto>>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<bool>> ApproveAsync(Guid applicationId, CancellationToken ct = default);
    Task<ApiResponse<bool>> RejectAsync(Guid applicationId, string reason, CancellationToken ct = default);
}

public sealed class HostApplicationService : IHostApplicationService
{
    private readonly NexumDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _email;
    private readonly ILogger<HostApplicationService> _logger;

    public HostApplicationService(NexumDbContext db, UserManager<ApplicationUser> userManager,
        IEmailService email, ILogger<HostApplicationService> logger)
    {
        _db = db;
        _userManager = userManager;
        _email = email;
        _logger = logger;
    }

    public async Task<ApiResponse<HostApplicationDto>> ApplyAsync(string userId,
        HostApplicationRequest request, CancellationToken ct = default)
    {
        var existing = await _db.HostApplications
            .FirstOrDefaultAsync(h => h.UserId == userId && h.Status == HostApplicationStatus.Pending, ct);
        if (existing is not null)
            return ApiResponse<HostApplicationDto>.Fail("APPLICATION_EXISTS",
                "You already have a pending host application.");

        var application = new HostApplication
        {
            UserId = userId,
            BusinessName = request.BusinessName,
            PhoneNumber = request.PhoneNumber,
            IdentityDocumentUrl = request.IdentityDocumentUrl
        };
        _db.HostApplications.Add(application);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Host application {AppId} submitted by {UserId}", application.Id, userId);
        return ApiResponse<HostApplicationDto>.Ok(await MapToDtoAsync(application, ct));
    }

    public async Task<ApiResponse<HostApplicationDto?>> GetMyApplicationAsync(string userId,
        CancellationToken ct = default)
    {
        var app = await _db.HostApplications
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync(h => h.UserId == userId, ct);
        return ApiResponse<HostApplicationDto?>.Ok(app is null ? null : await MapToDtoAsync(app, ct));
    }

    public async Task<ApiResponse<PagedResult<HostApplicationDto>>> ListAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.HostApplications.OrderByDescending(h => h.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var dtos = new List<HostApplicationDto>();
        foreach (var a in items) dtos.Add(await MapToDtoAsync(a, ct));
        return ApiResponse<PagedResult<HostApplicationDto>>.Ok(new PagedResult<HostApplicationDto>
        {
            Items = dtos, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<bool>> ApproveAsync(Guid applicationId, CancellationToken ct = default)
    {
        var application = await _db.HostApplications.FindAsync([applicationId], ct);
        if (application is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Application not found.");

        application.Status = HostApplicationStatus.Approved;
        application.UpdatedAt = DateTime.UtcNow;

        // Promote user to host role
        var user = await _userManager.FindByIdAsync(application.UserId);
        if (user is not null)
        {
            await _userManager.RemoveFromRoleAsync(user, Roles.Worshipper);
            await _userManager.AddToRoleAsync(user, Roles.Host);

            await _email.SendAsync(user.Email!, user.FullName,
                "Nexum — Host Application Approved",
                "<p>Your host application has been approved. You can now list properties on Nexum.</p>",
                ct);
        }

        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    public async Task<ApiResponse<bool>> RejectAsync(Guid applicationId, string reason,
        CancellationToken ct = default)
    {
        var application = await _db.HostApplications.FindAsync([applicationId], ct);
        if (application is null)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Application not found.");

        application.Status = HostApplicationStatus.Rejected;
        application.RejectionReason = reason;
        application.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var user = await _userManager.FindByIdAsync(application.UserId);
        if (user is not null)
        {
            await _email.SendAsync(user.Email!, user.FullName,
                "Nexum — Host Application Update",
                $"<p>Your host application was not approved at this time. Reason: {reason}</p>",
                ct);
        }

        return ApiResponse<bool>.Ok(true);
    }

    private async Task<HostApplicationDto> MapToDtoAsync(HostApplication a, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([a.UserId], ct);
        return new HostApplicationDto(a.Id, a.UserId, user?.FullName, a.BusinessName,
            a.PhoneNumber, a.IdentityDocumentUrl, a.Status.ToString(), a.RejectionReason, a.CreatedAt);
    }
}
