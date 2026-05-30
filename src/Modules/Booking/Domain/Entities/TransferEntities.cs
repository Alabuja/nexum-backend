namespace Nexum.Modules.Booking.Domain.Entities;

/// <summary>
/// Host's verified bank account for receiving payouts.
/// The recipient_code is created by Paystack and used for all transfers.
/// </summary>
public sealed class HostBankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HostId { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;        // e.g. "044" for Access Bank
    public string BankName { get; set; } = string.Empty;        // e.g. "Access Bank"
    public string AccountNumber { get; set; } = string.Empty;   // 10-digit NUBAN
    public string AccountName { get; set; } = string.Empty;     // Verified name from Paystack
    public string PaystackRecipientCode { get; set; } = string.Empty; // e.g. RCP_xxxxxxxxxx
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Tracks each payout transfer to a host.
/// Created when the 3-day hold period expires.
/// Updated by Paystack webhook on success/failure.
/// </summary>
public sealed class BookingTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string HostId { get; set; } = string.Empty;
    public Guid BankAccountId { get; set; }
    public decimal Amount { get; set; }             // Amount transferred (after any deductions)
    public string PaystackTransferCode { get; set; } = string.Empty;  // TRF_xxxxxxxxxx
    public string PaystackReference { get; set; } = string.Empty;
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime? TransferredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public BookingEntity Booking { get; set; } = null!;
    public HostBankAccount BankAccount { get; set; } = null!;
}

public enum TransferStatus
{
    Pending,    // Initiated, awaiting Paystack confirmation
    Success,    // Transfer completed
    Failed,     // Transfer failed
    Reversed,   // Transfer reversed by Paystack
}
