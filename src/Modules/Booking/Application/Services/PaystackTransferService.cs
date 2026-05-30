using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nexum.Modules.Booking.Application.Services;

// ── Result types ──────────────────────────────────────────────
public sealed record BankVerificationResult(
    bool Success,
    string? AccountName,
    string? AccountNumber,
    string? BankName,
    string? Message
);

public sealed record RecipientResult(
    bool Success,
    string? RecipientCode,
    string? Message
);

public sealed record TransferResult(
    bool Success,
    string? TransferCode,
    string? Reference,
    string? Message
);

public sealed record NigerianBank(string Code, string Name);

// ── Interface ─────────────────────────────────────────────────
public interface IPaystackTransferService
{
    Task<List<NigerianBank>> GetBanksAsync(CancellationToken ct = default);

    /// <summary>
    /// Verify a Nigerian bank account number using Paystack's bank resolve API.
    /// Returns the account holder's name as it appears at the bank.
    /// </summary>
    Task<BankVerificationResult> VerifyAccountAsync(
        string accountNumber, string bankCode, CancellationToken ct = default);

    /// <summary>
    /// Create a transfer recipient in Paystack using a verified bank account.
    /// Returns the recipient_code needed for all future transfers.
    /// </summary>
    Task<RecipientResult> CreateRecipientAsync(
        string accountName, string accountNumber, string bankCode,
        string hostEmail, CancellationToken ct = default);

    /// <summary>
    /// Initiate a transfer to a recipient.
    /// Amount is in Naira — converted to kobo internally.
    /// </summary>
    Task<TransferResult> InitiateTransferAsync(
        string recipientCode, decimal amountNaira,
        string reference, string reason, CancellationToken ct = default);

    bool VerifyTransferWebhookSignature(string payload, string signature);
}

// ── Implementation ────────────────────────────────────────────
public sealed class PaystackTransferService : IPaystackTransferService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<PaystackTransferService> _logger;

    private string SecretKey => _config["Paystack:SecretKey"]
        ?? throw new InvalidOperationException("Paystack:SecretKey not configured");

    public PaystackTransferService(HttpClient http, IConfiguration config,
        ILogger<PaystackTransferService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<List<NigerianBank>> GetBanksAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await SendAsync(HttpMethod.Get,
                "https://api.paystack.co/bank?currency=NGN&country=nigeria&perPage=200", ct: ct);
            var data = response.GetProperty("data");
            var banks = new List<NigerianBank>();
            foreach (var bank in data.EnumerateArray())
            {
                banks.Add(new NigerianBank(
                    bank.GetProperty("code").GetString()!,
                    bank.GetProperty("name").GetString()!
                ));
            }
            return banks.OrderBy(b => b.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bank list from Paystack");
            return [];
        }
    }

    public async Task<BankVerificationResult> VerifyAccountAsync(
        string accountNumber, string bankCode, CancellationToken ct = default)
    {
        // Validate NUBAN format before hitting the API
        if (!IsValidNuban(accountNumber))
            return new BankVerificationResult(false, null, null, null,
                "Account number must be exactly 10 digits.");

        if (string.IsNullOrWhiteSpace(bankCode))
            return new BankVerificationResult(false, null, null, null, "Bank code is required.");

        try
        {
            var url = $"https://api.paystack.co/bank/resolve?account_number={accountNumber}&bank_code={bankCode}";
            var response = await SendAsync(HttpMethod.Get, url, ct: ct);

            if (response.TryGetProperty("status", out var status) && status.GetBoolean())
            {
                var data = response.GetProperty("data");
                return new BankVerificationResult(
                    true,
                    data.GetProperty("account_name").GetString(),
                    data.GetProperty("account_number").GetString(),
                    null, // bank name not in resolve response
                    null);
            }

            var msg = response.TryGetProperty("message", out var m)
                ? m.GetString() : "Could not verify account";
            return new BankVerificationResult(false, null, null, null, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account verification failed for {AccountNumber}", accountNumber);
            return new BankVerificationResult(false, null, null, null,
                "Verification service unavailable. Please try again.");
        }
    }

    public async Task<RecipientResult> CreateRecipientAsync(
        string accountName, string accountNumber, string bankCode,
        string hostEmail, CancellationToken ct = default)
    {
        var body = new
        {
            type = "nuban",
            name = accountName,
            account_number = accountNumber,
            bank_code = bankCode,
            currency = "NGN",
            email = hostEmail,
            description = $"Nexum host payout — {accountName}"
        };

        try
        {
            var response = await SendAsync(HttpMethod.Post,
                "https://api.paystack.co/transferrecipient",
                body, ct);

            if (response.TryGetProperty("status", out var status) && status.GetBoolean())
            {
                var data = response.GetProperty("data");
                return new RecipientResult(
                    true,
                    data.GetProperty("recipient_code").GetString(),
                    null);
            }

            var msg = response.TryGetProperty("message", out var m)
                ? m.GetString() : "Failed to create recipient";
            return new RecipientResult(false, null, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Paystack recipient");
            return new RecipientResult(false, null, ex.Message);
        }
    }

    public async Task<TransferResult> InitiateTransferAsync(
        string recipientCode, decimal amountNaira,
        string reference, string reason, CancellationToken ct = default)
    {
        var body = new
        {
            source = "balance",
            amount = (long)(amountNaira * 100), // kobo
            reference,
            recipient = recipientCode,
            reason
        };

        try
        {
            var response = await SendAsync(HttpMethod.Post,
                "https://api.paystack.co/transfer", body, ct);

            if (response.TryGetProperty("status", out var status) && status.GetBoolean())
            {
                var data = response.GetProperty("data");
                return new TransferResult(
                    true,
                    data.GetProperty("transfer_code").GetString(),
                    data.GetProperty("reference").GetString(),
                    null);
            }

            var msg = response.TryGetProperty("message", out var m)
                ? m.GetString() : "Transfer failed";
            _logger.LogWarning("Paystack transfer failed: {Message}", msg);
            return new TransferResult(false, null, null, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer initiation exception for recipient {RecipientCode}", recipientCode);
            return new TransferResult(false, null, null, ex.Message);
        }
    }

    public bool VerifyTransferWebhookSignature(string payload, string signature)
    {
        var keyBytes = Encoding.UTF8.GetBytes(SecretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = System.Security.Cryptography.HMACSHA512.HashData(keyBytes, payloadBytes);
        var computed = Convert.ToHexString(hash).ToLower();
        return computed == signature.ToLower();
    }

    // ── Helpers ──────────────────────────────────────────────
    private async Task<JsonElement> SendAsync(HttpMethod method, string url,
        object? body = null, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Nigerian NUBAN account numbers are exactly 10 digits.
    /// </summary>
    private static bool IsValidNuban(string accountNumber) =>
        !string.IsNullOrWhiteSpace(accountNumber) &&
        accountNumber.Length == 10 &&
        accountNumber.All(char.IsDigit);
}
