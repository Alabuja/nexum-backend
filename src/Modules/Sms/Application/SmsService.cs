using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nexum.Modules.Auth.Infrastructure.Persistence;
using Nexum.Modules.Booking.Application.Services;
using Nexum.Modules.Sms.Domain.Entities;
using Nexum.SharedKernel.Models;
using System.ComponentModel.DataAnnotations;

namespace Nexum.Modules.Sms.Application;

public sealed record SmsWalletDto(int Balance, DateTime? UpdatedAt);

public sealed record AdminSmsWalletDto(
    string UserId, string? FullName, string? Email, string? PhoneNumber,
    int Balance, DateTime? UpdatedAt
);

public sealed record SmsTransactionDto(
    Guid Id, string Type, int Amount, int BalanceAfter,
    string? Reference, string? Description, DateTime CreatedAt
);

public sealed record SmsMessageDto(
    Guid Id, string Mode, string Recipient, string Status,
    string? ProviderMessageId, string? ErrorMessage, DateTime CreatedAt, DateTime? SentAt
);

public sealed record AdminSmsMessageDto(
    Guid Id, string UserId, string? FullName, string Mode, string Recipient, string Status,
    string? ProviderMessageId, string? ErrorMessage, DateTime CreatedAt, DateTime? SentAt
);

public sealed record SmsTopupDto(
    Guid Id, string UserId, int Tokens, decimal AmountNaira, string Status,
    string PaystackReference, DateTime CreatedAt, DateTime? PaidAt, DateTime? CreditedAt
);

public sealed record InitiateSmsTopupRequest(
    [Range(1, int.MaxValue)] int Tokens,
    [Required] string CallbackUrl
);

public sealed record InitiateSmsTopupResponse(
    string AuthorizationUrl,
    string Reference,
    int Tokens,
    decimal AmountNaira
);

public sealed record VerifySmsTopupRequest([Required] string Reference);

public sealed record SendSmsRequest(
    [Required] string Recipient,
    [Required] string Body,
    string? Reference = null
);

public sealed record PhoneFallbackLogRequest(
    [Required] string Recipient,
    [Required] string Body,
    string? Reference = null
);

public sealed record CreditWalletRequest(
    [Range(1, int.MaxValue)] int Amount,
    string? Reference,
    string? Description
);

public interface ISmsService
{
    Task<ApiResponse<SmsWalletDto>> GetWalletAsync(string userId, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SmsTransactionDto>>> GetTransactionsAsync(string userId, int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<SmsMessageDto>> SendNexumPaidAsync(string userId, SendSmsRequest request, CancellationToken ct = default);
    Task<ApiResponse<SmsMessageDto>> SendWithTokensAsync(string userId, SendSmsRequest request, CancellationToken ct = default);
    Task<ApiResponse<SmsWalletDto>> CreditWalletAsync(string userId, CreditWalletRequest request, CancellationToken ct = default);
    Task<ApiResponse<SmsMessageDto>> LogPhoneFallbackAsync(string userId, PhoneFallbackLogRequest request, CancellationToken ct = default);
    Task<ApiResponse<InitiateSmsTopupResponse>> InitiateTopupAsync(string userId, InitiateSmsTopupRequest request, CancellationToken ct = default);
    Task<ApiResponse<SmsWalletDto>> VerifyTopupAsync(string userId, VerifySmsTopupRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AdminSmsWalletDto>>> AdminWalletsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<AdminSmsMessageDto>>> AdminMessagesAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<SmsTransactionDto>>> AdminTransactionsAsync(int page, int pageSize, CancellationToken ct = default);
}

public sealed class SmsService : ISmsService
{
    private readonly NexumDbContext _db;
    private readonly ITermiiSmsService _termii;
    private readonly IPaystackService _paystack;
    private readonly IConfiguration _config;

    public SmsService(NexumDbContext db, ITermiiSmsService termii,
        IPaystackService paystack, IConfiguration config)
    {
        _db = db;
        _termii = termii;
        _paystack = paystack;
        _config = config;
    }

    public async Task<ApiResponse<SmsWalletDto>> GetWalletAsync(string userId, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SmsWalletDto>.Ok(MapWallet(wallet));
    }

    public async Task<ApiResponse<PagedResult<SmsTransactionDto>>> GetTransactionsAsync(
        string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SmsTransactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return ApiResponse<PagedResult<SmsTransactionDto>>.Ok(new PagedResult<SmsTransactionDto>
        {
            Items = items.Select(MapTransaction).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SmsMessageDto>> SendNexumPaidAsync(
        string userId, SendSmsRequest request, CancellationToken ct = default)
    {
        var message = CreateMessage(userId, SmsSendMode.NexumPaid, request.Recipient, request.Body);
        _db.SmsMessages.Add(message);

        var result = await _termii.SendAsync(request.Recipient, request.Body, ct);
        ApplyProviderResult(message, result);
        await _db.SaveChangesAsync(ct);

        return result.Success
            ? ApiResponse<SmsMessageDto>.Ok(MapMessage(message))
            : ApiResponse<SmsMessageDto>.Fail("SMS_SEND_FAILED",
                result.ErrorMessage ?? "SMS could not be sent.", MapMessage(message));
    }

    public async Task<ApiResponse<SmsMessageDto>> SendWithTokensAsync(
        string userId, SendSmsRequest request, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(userId, ct);
        var tokenCost = int.TryParse(_config["Sms:TokenCostPerMessage"], out var configuredCost)
            ? Math.Max(1, configuredCost)
            : 1;

        if (wallet.Balance < tokenCost)
            return ApiResponse<SmsMessageDto>.Fail("INSUFFICIENT_SMS_TOKENS",
                "You do not have enough SMS tokens.");

        var message = CreateMessage(userId, SmsSendMode.Token, request.Recipient, request.Body);
        _db.SmsMessages.Add(message);

        var result = await _termii.SendAsync(request.Recipient, request.Body, ct);
        ApplyProviderResult(message, result);

        if (result.Success)
        {
            wallet.Balance -= tokenCost;
            wallet.UpdatedAt = DateTime.UtcNow;
            _db.SmsTransactions.Add(new SmsTransaction
            {
                UserId = userId,
                Type = SmsTransactionType.Debit,
                Amount = -tokenCost,
                BalanceAfter = wallet.Balance,
                Reference = request.Reference ?? message.Id.ToString(),
                Description = $"SMS sent to {request.Recipient}"
            });
        }

        await _db.SaveChangesAsync(ct);

        return result.Success
            ? ApiResponse<SmsMessageDto>.Ok(MapMessage(message))
            : ApiResponse<SmsMessageDto>.Fail("SMS_SEND_FAILED",
                result.ErrorMessage ?? "SMS could not be sent.", MapMessage(message));
    }

    public async Task<ApiResponse<SmsWalletDto>> CreditWalletAsync(
        string userId, CreditWalletRequest request, CancellationToken ct = default)
    {
        var wallet = await GetOrCreateWalletAsync(userId, ct);
        wallet.Balance += request.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;

        _db.SmsTransactions.Add(new SmsTransaction
        {
            UserId = userId,
            Type = SmsTransactionType.Credit,
            Amount = request.Amount,
            BalanceAfter = wallet.Balance,
            Reference = request.Reference,
            Description = request.Description ?? "SMS wallet credited"
        });

        await _db.SaveChangesAsync(ct);
        return ApiResponse<SmsWalletDto>.Ok(MapWallet(wallet));
    }

    public async Task<ApiResponse<SmsMessageDto>> LogPhoneFallbackAsync(
        string userId, PhoneFallbackLogRequest request, CancellationToken ct = default)
    {
        var message = CreateMessage(userId, SmsSendMode.PhoneFallback, request.Recipient, request.Body);
        message.Status = "OpenedOnDevice";
        message.SentAt = DateTime.UtcNow;
        _db.SmsMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SmsMessageDto>.Ok(MapMessage(message));
    }

    public async Task<ApiResponse<InitiateSmsTopupResponse>> InitiateTopupAsync(
        string userId, InitiateSmsTopupRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.Email is null)
            return ApiResponse<InitiateSmsTopupResponse>.Fail("USER_EMAIL_REQUIRED",
                "A verified email is required to buy SMS tokens.");

        var tokenPrice = decimal.TryParse(_config["Sms:TokenPriceNaira"], out var configuredPrice)
            ? Math.Max(1m, configuredPrice)
            : 20m;
        var amount = tokenPrice * request.Tokens;
        var reference = $"SMS-{Guid.NewGuid():N}";

        var init = await _paystack.InitializeTransactionAsync(
            user.Email, amount, reference, request.CallbackUrl, ct);

        if (!init.Success || init.AuthorizationUrl is null || init.Reference is null)
            return ApiResponse<InitiateSmsTopupResponse>.Fail("PAYSTACK_INIT_FAILED",
                init.Message ?? "Could not initialize SMS token payment.");

        _db.SmsTopups.Add(new SmsTopup
        {
            UserId = userId,
            PaystackReference = init.Reference,
            Tokens = request.Tokens,
            AmountNaira = amount,
            Status = SmsTopupStatus.Pending
        });
        await _db.SaveChangesAsync(ct);

        return ApiResponse<InitiateSmsTopupResponse>.Ok(
            new InitiateSmsTopupResponse(init.AuthorizationUrl, init.Reference,
                request.Tokens, amount));
    }

    public async Task<ApiResponse<SmsWalletDto>> VerifyTopupAsync(
        string userId, VerifySmsTopupRequest request, CancellationToken ct = default)
    {
        var topup = await _db.SmsTopups
            .FirstOrDefaultAsync(t => t.PaystackReference == request.Reference && t.UserId == userId, ct);
        if (topup is null)
            return ApiResponse<SmsWalletDto>.Fail("TOPUP_NOT_FOUND", "SMS top-up was not found.");

        var wallet = await GetOrCreateWalletAsync(userId, ct);
        if (topup.Status == SmsTopupStatus.Paid)
            return ApiResponse<SmsWalletDto>.Ok(MapWallet(wallet));

        var verify = await _paystack.VerifyTransactionAsync(request.Reference, ct);
        if (!verify.Success)
        {
            topup.Status = SmsTopupStatus.Failed;
            await _db.SaveChangesAsync(ct);
            return ApiResponse<SmsWalletDto>.Fail("PAYMENT_NOT_CONFIRMED",
                verify.Message ?? "Payment has not been confirmed.");
        }

        if (verify.AmountNaira < topup.AmountNaira)
            return ApiResponse<SmsWalletDto>.Fail("PAYMENT_AMOUNT_MISMATCH",
                "Payment amount does not match this SMS token purchase.");

        wallet.Balance += topup.Tokens;
        wallet.UpdatedAt = DateTime.UtcNow;
        topup.Status = SmsTopupStatus.Paid;
        topup.PaidAt = DateTime.UtcNow;
        topup.CreditedAt = DateTime.UtcNow;

        _db.SmsTransactions.Add(new SmsTransaction
        {
            UserId = userId,
            Type = SmsTransactionType.Credit,
            Amount = topup.Tokens,
            BalanceAfter = wallet.Balance,
            Reference = topup.PaystackReference,
            Description = $"Purchased {topup.Tokens} SMS tokens"
        });

        await _db.SaveChangesAsync(ct);
        return ApiResponse<SmsWalletDto>.Ok(MapWallet(wallet));
    }

    public async Task<ApiResponse<PagedResult<AdminSmsWalletDto>>> AdminWalletsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SmsWallets.OrderByDescending(w => w.UpdatedAt);
        var total = await query.CountAsync(ct);
        var wallets = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var userIds = wallets.Select(w => w.UserId).ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var items = wallets.Select(w =>
        {
            users.TryGetValue(w.UserId, out var user);
            return new AdminSmsWalletDto(w.UserId, user?.FullName, user?.Email,
                user?.PhoneNumber, w.Balance, w.UpdatedAt);
        }).ToList();

        return ApiResponse<PagedResult<AdminSmsWalletDto>>.Ok(new PagedResult<AdminSmsWalletDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<AdminSmsMessageDto>>> AdminMessagesAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SmsMessages.OrderByDescending(m => m.CreatedAt);
        var total = await query.CountAsync(ct);
        var messages = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var userIds = messages.Select(m => m.UserId).Distinct().ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var items = messages.Select(m =>
        {
            users.TryGetValue(m.UserId, out var user);
            return new AdminSmsMessageDto(m.Id, m.UserId, user?.FullName,
                m.Mode.ToString(), m.Recipient, m.Status, m.ProviderMessageId,
                m.ErrorMessage, m.CreatedAt, m.SentAt);
        }).ToList();

        return ApiResponse<PagedResult<AdminSmsMessageDto>>.Ok(new PagedResult<AdminSmsMessageDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<SmsTransactionDto>>> AdminTransactionsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.SmsTransactions.OrderByDescending(t => t.CreatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return ApiResponse<PagedResult<SmsTransactionDto>>.Ok(new PagedResult<SmsTransactionDto>
        {
            Items = items.Select(MapTransaction).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    private async Task<SmsWallet> GetOrCreateWalletAsync(string userId, CancellationToken ct)
    {
        var wallet = await _db.SmsWallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is not null) return wallet;

        wallet = new SmsWallet { UserId = userId };
        _db.SmsWallets.Add(wallet);
        return wallet;
    }

    private static SmsMessage CreateMessage(string userId, SmsSendMode mode, string recipient, string body) =>
        new()
        {
            UserId = userId,
            Mode = mode,
            Recipient = recipient.Trim(),
            Body = body.Trim()
        };

    private static void ApplyProviderResult(SmsMessage message, TermiiSmsResult result)
    {
        message.ProviderMessageId = result.MessageId;
        message.Status = result.Success ? "Sent" : "Failed";
        message.ErrorMessage = result.ErrorMessage;
        message.SentAt = result.Success ? DateTime.UtcNow : null;
    }

    private static SmsWalletDto MapWallet(SmsWallet wallet) =>
        new(wallet.Balance, wallet.UpdatedAt);

    private static SmsTransactionDto MapTransaction(SmsTransaction tx) =>
        new(tx.Id, tx.Type.ToString(), tx.Amount, tx.BalanceAfter,
            tx.Reference, tx.Description, tx.CreatedAt);

    private static SmsMessageDto MapMessage(SmsMessage message) =>
        new(message.Id, message.Mode.ToString(), message.Recipient, message.Status,
            message.ProviderMessageId, message.ErrorMessage, message.CreatedAt, message.SentAt);
}
