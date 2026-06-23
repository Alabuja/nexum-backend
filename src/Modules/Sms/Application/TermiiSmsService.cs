using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Nexum.Modules.Sms.Application;

public sealed record TermiiSmsResult(
    bool Success,
    string? MessageId,
    string? ProviderMessage,
    string? ErrorMessage
);

public interface ITermiiSmsService
{
    Task<TermiiSmsResult> SendAsync(string recipient, string body, CancellationToken ct = default);
}

public sealed class TermiiSmsService : ITermiiSmsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<TermiiSmsService> _logger;

    public TermiiSmsService(HttpClient http, IConfiguration config, ILogger<TermiiSmsService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<TermiiSmsResult> SendAsync(string recipient, string body, CancellationToken ct = default)
    {
        var apiKey = _config["Termii:ApiKey"]
            ?? throw new InvalidOperationException("Termii:ApiKey not configured");
        var senderId = _config["Termii:SenderId"] ?? "Nexum";
        var baseUrl = (_config["Termii:BaseUrl"] ?? "https://api.ng.termii.com/api").TrimEnd('/');

        var payload = new
        {
            to = NormalizePhone(recipient),
            from = senderId,
            sms = body,
            type = "plain",
            channel = _config["Termii:Channel"] ?? "generic",
            api_key = apiKey
        };

        try
        {
            var response = await _http.PostAsJsonAsync($"{baseUrl}/sms/send", payload, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Termii SMS failed with {Status}: {Body}",
                    response.StatusCode, content);
                return new TermiiSmsResult(false, null, null, content);
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var messageId = TryReadString(root, "message_id");
            var message = TryReadString(root, "message") ?? TryReadString(root, "status");

            return new TermiiSmsResult(true, messageId, message, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Termii SMS exception for {Recipient}", recipient);
            return new TermiiSmsResult(false, null, null, ex.Message);
        }
    }

    private static string NormalizePhone(string phone)
    {
        var value = phone.Trim().Replace(" ", "").Replace("-", "");
        if (value.StartsWith("+")) return value;
        if (value.StartsWith("0") && value.Length == 11) return $"+234{value[1..]}";
        return value;
    }

    private static string? TryReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;
}
