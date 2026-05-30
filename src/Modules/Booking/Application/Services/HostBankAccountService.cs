using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Domain.Entities;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Booking.Application.Services;

// ── DTOs ──────────────────────────────────────────────────────
public sealed record AddBankAccountRequest(
    [Required] string BankCode,
    [Required, StringLength(10, MinimumLength = 10)] string AccountNumber
);

public sealed record BankAccountDto(
    Guid Id,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    bool IsActive,
    DateTime CreatedAt
);

public sealed record BankDto(string Code, string Name);

public sealed record VerifyAccountRequest(
    [Required] string BankCode,
    [Required, StringLength(10, MinimumLength = 10)] string AccountNumber
);

public sealed record VerifyAccountResponse(
    string AccountName,
    string AccountNumber,
    string BankCode,
    string BankName
);

// ── Service ───────────────────────────────────────────────────
public interface IHostBankAccountService
{
    Task<ApiResponse<List<BankDto>>> GetBanksAsync(CancellationToken ct = default);
    Task<ApiResponse<VerifyAccountResponse>> VerifyAccountAsync(VerifyAccountRequest request, CancellationToken ct = default);
    Task<ApiResponse<BankAccountDto>> AddBankAccountAsync(string hostId, string hostEmail, AddBankAccountRequest request, CancellationToken ct = default);
    Task<ApiResponse<List<BankAccountDto>>> GetMyBankAccountsAsync(string hostId, CancellationToken ct = default);
    Task<ApiResponse<bool>> RemoveBankAccountAsync(string hostId, Guid accountId, CancellationToken ct = default);
}

public sealed class HostBankAccountService : IHostBankAccountService
{
    private readonly NexumDbContext _db;
    private readonly IPaystackTransferService _transfer;
    private readonly ILogger<HostBankAccountService> _logger;

    public HostBankAccountService(NexumDbContext db, IPaystackTransferService transfer,
        ILogger<HostBankAccountService> logger)
    {
        _db = db;
        _transfer = transfer;
        _logger = logger;
    }

    public async Task<ApiResponse<List<BankDto>>> GetBanksAsync(CancellationToken ct = default)
    {
        var banks = await _transfer.GetBanksAsync(ct);
        return ApiResponse<List<BankDto>>.Ok(
            banks.Select(b => new BankDto(b.Code, b.Name)).ToList());
    }

    public async Task<ApiResponse<VerifyAccountResponse>> VerifyAccountAsync(
        VerifyAccountRequest request, CancellationToken ct = default)
    {
        var result = await _transfer.VerifyAccountAsync(request.AccountNumber, request.BankCode, ct);

        if (!result.Success)
            return ApiResponse<VerifyAccountResponse>.Fail("ACCOUNT_VERIFICATION_FAILED",
                result.Message ?? "Could not verify account details.");

        // Get bank name
        var banks = await _transfer.GetBanksAsync(ct);
        var bankName = banks.FirstOrDefault(b => b.Code == request.BankCode)?.Name ?? request.BankCode;

        return ApiResponse<VerifyAccountResponse>.Ok(new VerifyAccountResponse(
            result.AccountName!,
            result.AccountNumber!,
            request.BankCode,
            bankName));
    }

    public async Task<ApiResponse<BankAccountDto>> AddBankAccountAsync(
        string hostId, string hostEmail, AddBankAccountRequest request,
        CancellationToken ct = default)
    {
        // Check for duplicates
        var existing = await _db.HostBankAccounts
            .Where(b => b.HostId == hostId &&
                        b.AccountNumber == request.AccountNumber &&
                        b.BankCode == request.BankCode &&
                        b.IsActive)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return ApiResponse<BankAccountDto>.Fail("ACCOUNT_ALREADY_EXISTS",
                "This bank account is already saved.");

        // Step 1: Verify account with Paystack
        var verification = await _transfer.VerifyAccountAsync(
            request.AccountNumber, request.BankCode, ct);

        if (!verification.Success)
            return ApiResponse<BankAccountDto>.Fail("ACCOUNT_VERIFICATION_FAILED",
                verification.Message ?? "Bank account verification failed. Check your account number and bank.");

        // Step 2: Get bank name
        var banks = await _transfer.GetBanksAsync(ct);
        var bankName = banks.FirstOrDefault(b => b.Code == request.BankCode)?.Name ?? request.BankCode;

        // Step 3: Create Paystack transfer recipient
        var recipientResult = await _transfer.CreateRecipientAsync(
            verification.AccountName!,
            request.AccountNumber,
            request.BankCode,
            hostEmail, ct);

        if (!recipientResult.Success)
            return ApiResponse<BankAccountDto>.Fail("RECIPIENT_CREATION_FAILED",
                recipientResult.Message ?? "Failed to register bank account for transfers.");

        // Step 4: Save to database
        var account = new HostBankAccount
        {
            HostId = hostId,
            BankCode = request.BankCode,
            BankName = bankName,
            AccountNumber = request.AccountNumber,
            AccountName = verification.AccountName!,
            PaystackRecipientCode = recipientResult.RecipientCode!,
            IsActive = true
        };
        _db.HostBankAccounts.Add(account);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Bank account added for host {HostId}: {BankName} {AccountNumber} ({AccountName})",
            hostId, bankName, request.AccountNumber, verification.AccountName);

        return ApiResponse<BankAccountDto>.Ok(MapToDto(account));
    }

    public async Task<ApiResponse<List<BankAccountDto>>> GetMyBankAccountsAsync(
        string hostId, CancellationToken ct = default)
    {
        var accounts = await _db.HostBankAccounts
            .Where(b => b.HostId == hostId && b.IsActive)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
        return ApiResponse<List<BankAccountDto>>.Ok(accounts.Select(MapToDto).ToList());
    }

    public async Task<ApiResponse<bool>> RemoveBankAccountAsync(string hostId, Guid accountId,
        CancellationToken ct = default)
    {
        var account = await _db.HostBankAccounts.FindAsync([accountId], ct);
        if (account is null || account.HostId != hostId)
            return ApiResponse<bool>.Fail("NOT_FOUND", "Bank account not found.");

        // Check for pending transfers
        var pendingTransfers = await _db.BookingTransfers
            .AnyAsync(t => t.BankAccountId == accountId && t.Status == TransferStatus.Pending, ct);

        if (pendingTransfers)
            return ApiResponse<bool>.Fail("PENDING_TRANSFERS",
                "Cannot remove account with pending transfers.");

        account.IsActive = false;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ApiResponse<bool>.Ok(true);
    }

    private static BankAccountDto MapToDto(HostBankAccount a) =>
        new(a.Id, a.BankCode, a.BankName, a.AccountNumber, a.AccountName, a.IsActive, a.CreatedAt);
}
