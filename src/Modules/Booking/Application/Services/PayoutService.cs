using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.Modules.Booking.Domain.Enums;
using Nexum.SharedKernel.Interfaces;
using Nexum.SharedKernel.Models;
using System.Text.Json;

namespace Nexum.Modules.Booking.Application.Services;

public interface IPayoutService
{
    /// <summary>Called by Hangfire daily at 06:00 WAT.</summary>
    Task ProcessDuePayoutsAsync(CancellationToken ct = default);

    /// <summary>Handle Paystack transfer.success / transfer.failed webhooks.</summary>
    Task<ApiResponse<bool>> HandleTransferWebhookAsync(string payload, string signature, CancellationToken ct = default);
}

public sealed class PayoutService : IPayoutService
{
    private readonly NexumDbContext _db;
    private readonly IPaystackTransferService _transfer;
    private readonly IEmailService _email;
    private readonly ILogger<PayoutService> _logger;

    /// <summary>
    /// Hold period in days before payout is initiated.
    /// 3 days gives time for disputes / chargebacks from Paystack.
    /// </summary>
    private const int HoldDays = 3;

    public PayoutService(NexumDbContext db, IPaystackTransferService transfer,
        IEmailService email, ILogger<PayoutService> logger)
    {
        _db = db;
        _transfer = transfer;
        _email = email;
        _logger = logger;
    }

    public async Task ProcessDuePayoutsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-HoldDays);

        // Find confirmed bookings paid 3+ days ago with no transfer yet
        var due = await _db.Bookings
            .Where(b =>
                (b.Status == BookingStatus.Confirmed ||
                 b.Status == BookingStatus.CheckedIn ||
                 b.Status == BookingStatus.Completed) &&
                b.PaidAt.HasValue &&
                b.PaidAt.Value <= cutoff &&
                !_db.BookingTransfers.Any(t => t.BookingId == b.Id &&
                    (t.Status == TransferStatus.Pending || t.Status == TransferStatus.Success)))
            .Include(b => b.Property)
            .ToListAsync(ct);

        if (!due.Any())
        {
            _logger.LogInformation("Payout job: no payouts due");
            return;
        }

        _logger.LogInformation("Payout job: processing {Count} due payouts", due.Count);

        foreach (var booking in due)
        {
            await ProcessSinglePayoutAsync(booking, ct);
        }
    }

    private async Task ProcessSinglePayoutAsync(BookingEntity booking, CancellationToken ct)
    {
        var hostId = booking.Property.HostId;

        // Find the host's active bank account
        var bankAccount = await _db.HostBankAccounts
            .Where(b => b.HostId == hostId && b.IsActive)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (bankAccount is null)
        {
            _logger.LogWarning(
                "Payout skipped for booking {BookingId}: host {HostId} has no bank account",
                booking.Id, hostId);
            return;
        }

        var reference = $"NEXUM-PAYOUT-{booking.Id:N}".ToUpper()[..30];
        var room = await _db.RoomTypes.FindAsync([booking.RoomTypeId], ct);
        var reason = $"Nexum payout — {room?.Name ?? "booking"} " +
                     $"{booking.CheckInDate:dd MMM} to {booking.CheckOutDate:dd MMM yyyy}";

        // Initiate transfer
        var result = await _transfer.InitiateTransferAsync(
            bankAccount.PaystackRecipientCode,
            booking.TotalAmount,
            reference,
            reason, ct);

        var transferRecord = new BookingTransfer
        {
            BookingId = booking.Id,
            HostId = hostId,
            BankAccountId = bankAccount.Id,
            Amount = booking.TotalAmount,
            PaystackReference = reference,
            PaystackTransferCode = result.TransferCode ?? string.Empty,
            Status = result.Success ? TransferStatus.Pending : TransferStatus.Failed,
            FailureReason = result.Success ? null : result.Message,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.BookingTransfers.Add(transferRecord);
        await _db.SaveChangesAsync(ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "Transfer initiated for booking {BookingId}: ₦{Amount} to {BankName} {AccountNumber}. Code: {TransferCode}",
                booking.Id, booking.TotalAmount, bankAccount.BankName,
                bankAccount.AccountNumber, result.TransferCode);
        }
        else
        {
            _logger.LogError(
                "Transfer FAILED for booking {BookingId}: {Reason}",
                booking.Id, result.Message);

            // Notify host of failure
            var host = await _db.Users.FindAsync([hostId], ct);
            if (host?.Email is not null)
            {
                await _email.SendAsync(host.Email, host.FullName,
                    "Nexum — Payout Transfer Failed",
                    $"""
                    <p>We were unable to process your payout for booking 
                    <strong>{booking.CheckInDate:dd MMM} – {booking.CheckOutDate:dd MMM yyyy}</strong>.</p>
                    <p>Amount: <strong>₦{booking.TotalAmount:N0}</strong></p>
                    <p>Reason: {result.Message}</p>
                    <p>Please check your bank account details in the portal and contact support if the issue persists.</p>
                    """, ct);
            }
        }
    }

    public async Task<ApiResponse<bool>> HandleTransferWebhookAsync(
        string payload, string signature, CancellationToken ct = default)
    {
        if (!_transfer.VerifyTransferWebhookSignature(payload, signature))
        {
            _logger.LogWarning("Transfer webhook signature mismatch");
            return ApiResponse<bool>.Fail("INVALID_SIGNATURE", "Signature mismatch.");
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(payload).RootElement;
        }
        catch
        {
            return ApiResponse<bool>.Fail("INVALID_PAYLOAD", "Could not parse payload.");
        }

        var eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        if (eventType is not "transfer.success" and not "transfer.failed" and not "transfer.reversed")
            return ApiResponse<bool>.Ok(true); // Acknowledge, ignore

        var data = root.GetProperty("data");
        var transferCode = data.TryGetProperty("transfer_code", out var tc) ? tc.GetString() : null;
        var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;

        if (transferCode is null && reference is null)
            return ApiResponse<bool>.Ok(true);

        var transfer = await _db.BookingTransfers
            .Include(t => t.Booking)
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t =>
                t.PaystackTransferCode == transferCode ||
                t.PaystackReference == reference, ct);

        if (transfer is null)
        {
            _logger.LogWarning("Transfer webhook: no record found for code {Code}", transferCode);
            return ApiResponse<bool>.Ok(true);
        }

        transfer.Status = eventType switch
        {
            "transfer.success"  => TransferStatus.Success,
            "transfer.failed"   => TransferStatus.Failed,
            "transfer.reversed" => TransferStatus.Reversed,
            _                   => transfer.Status
        };
        transfer.UpdatedAt = DateTime.UtcNow;
        if (transfer.Status == TransferStatus.Success)
            transfer.TransferredAt = DateTime.UtcNow;
        if (transfer.Status == TransferStatus.Failed)
            transfer.FailureReason = data.TryGetProperty("reason", out var reason)
                ? reason.GetString() : "Transfer failed";

        await _db.SaveChangesAsync(ct);

        // Notify host
        var host = await _db.Users.FindAsync([transfer.HostId], ct);
        if (host?.Email is not null)
        {
            if (transfer.Status == TransferStatus.Success)
            {
                await _email.SendAsync(host.Email, host.FullName,
                    "Nexum — Payout Successful 💰",
                    $"""
                    <p>Your payout of <strong>₦{transfer.Amount:N0}</strong> has been successfully 
                    transferred to <strong>{transfer.BankAccount.BankName}</strong> 
                    ({transfer.BankAccount.AccountNumber}).</p>
                    <p>Booking period: {transfer.Booking.CheckInDate:dd MMM} – 
                    {transfer.Booking.CheckOutDate:dd MMM yyyy}</p>
                    <p>Transfer code: <code>{transfer.PaystackTransferCode}</code></p>
                    """, ct);
            }
            else if (transfer.Status == TransferStatus.Failed)
            {
                await _email.SendAsync(host.Email, host.FullName,
                    "Nexum — Payout Failed",
                    $"""
                    <p>Your payout of <strong>₦{transfer.Amount:N0}</strong> could not be completed.</p>
                    <p>Reason: {transfer.FailureReason}</p>
                    <p>Please verify your bank account details or contact support.</p>
                    """, ct);
            }
        }

        _logger.LogInformation(
            "Transfer {TransferCode} status updated to {Status}",
            transferCode, transfer.Status);

        return ApiResponse<bool>.Ok(true);
    }
}
