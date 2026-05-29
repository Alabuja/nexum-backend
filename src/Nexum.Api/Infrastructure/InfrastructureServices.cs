using FirebaseAdmin.Messaging;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Nexum.SharedKernel.Interfaces;
using System.Security.Claims;

namespace Nexum.Api.Infrastructure;

public sealed class FcmNotificationService : INotificationService
{
    private readonly ILogger<FcmNotificationService> _logger;
    public FcmNotificationService(ILogger<FcmNotificationService> logger) => _logger = logger;

    public async Task SendPushAsync(string fcmToken, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken ct = default)
    {
        try
        {
            var message = new Message
            {
                Token = fcmToken,
                Notification = new Notification { Title = title, Body = body },
                Data = data,
                Android = new AndroidConfig { Priority = Priority.High }
            };
            await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
            _logger.LogDebug("FCM push sent to token {Token}", fcmToken[..10]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM push failed for token {Token}", fcmToken[..10]);
        }
    }

    public async Task SendPushToManyAsync(IEnumerable<string> fcmTokens, string title, string body,
        Dictionary<string, string>? data = null, CancellationToken ct = default)
    {
        var tokens = fcmTokens.ToList();
        // FCM batch allows up to 500 per call
        foreach (var batch in tokens.Chunk(500))
        {
            try
            {
                var message = new MulticastMessage
                {
                    Tokens = batch,
                    Notification = new Notification { Title = title, Body = body },
                    Data = data,
                    Android = new AndroidConfig { Priority = Priority.High }
                };
                var result = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message, ct);
                _logger.LogInformation("FCM multicast: {Success}/{Total} delivered",
                    result.SuccessCount, batch.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM multicast batch failed");
            }
        }
    }
}

public sealed class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string toName, string subject,
        string htmlBody, CancellationToken ct = default)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Email:FromName"] ?? "Nexum",
                _config["Email:FromAddress"] ?? "noreply@nexum.ng"));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"] ?? "587"),
                MailKit.Security.SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(_config["Email:SmtpUser"], _config["Email:SmtpPass"], ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed to {Email}", toEmail);
        }
    }
}

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public string Id => _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    public string Email => _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    public string Role => _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
