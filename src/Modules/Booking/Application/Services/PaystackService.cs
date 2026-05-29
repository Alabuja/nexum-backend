using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexum.Modules.Booking.Application.Services;

public interface IPaystackService
{
    Task<PaystackInitializeResult> InitializeTransactionAsync(
        string email, decimal amountNaira, string reference,
        string callbackUrl, CancellationToken ct = default);

    bool VerifyWebhookSignature(string payload, string signature);
}

public sealed record PaystackInitializeResult(
    bool Success,
    string? AuthorizationUrl,
    string? Reference,
    string? Message
);

public sealed class PaystackService : IPaystackService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<PaystackService> _logger;

    public PaystackService(HttpClient http, IConfiguration config, ILogger<PaystackService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<PaystackInitializeResult> InitializeTransactionAsync(
        string email, decimal amountNaira, string reference,
        string callbackUrl, CancellationToken ct = default)
    {
        var secretKey = _config["Paystack:SecretKey"]
            ?? throw new InvalidOperationException("Paystack:SecretKey not configured");

        var body = new
        {
            email,
            amount = (long)(amountNaira * 100), // Paystack uses kobo
            reference,
            callback_url = callbackUrl,
            channels = new[] { "card", "bank", "ussd", "mobile_money" }
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://api.paystack.co/transaction/initialize")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", secretKey);

        try
        {
            var response = await _http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("status").GetBoolean())
            {
                var data = root.GetProperty("data");
                return new PaystackInitializeResult(
                    true,
                    data.GetProperty("authorization_url").GetString(),
                    data.GetProperty("reference").GetString(),
                    null);
            }

            var message = root.TryGetProperty("message", out var msg)
                ? msg.GetString() : "Paystack initialization failed";
            _logger.LogWarning("Paystack init failed: {Message}", message);
            return new PaystackInitializeResult(false, null, null, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack initialization exception");
            return new PaystackInitializeResult(false, null, null, ex.Message);
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        var secretKey = _config["Paystack:SecretKey"] ?? string.Empty;
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA512.HashData(keyBytes, payloadBytes);
        var computed = Convert.ToHexString(hash).ToLower();
        return computed == signature.ToLower();
    }
}
