namespace Nexum.Modules.Sms.Domain.Entities;

public sealed class SmsWallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public int Balance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SmsTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public SmsTransactionType Type { get; set; }
    public int Amount { get; set; }
    public int BalanceAfter { get; set; }
    public string? Reference { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SmsMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public SmsSendMode Mode { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

public sealed class SmsTopup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string PaystackReference { get; set; } = string.Empty;
    public int Tokens { get; set; }
    public decimal AmountNaira { get; set; }
    public SmsTopupStatus Status { get; set; } = SmsTopupStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? CreditedAt { get; set; }
}

public enum SmsTransactionType
{
    Credit,
    Debit,
    Refund
}

public enum SmsSendMode
{
    PhoneFallback,
    NexumPaid,
    Token
}

public enum SmsTopupStatus
{
    Pending,
    Paid,
    Failed
}
