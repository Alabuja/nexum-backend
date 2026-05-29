namespace Nexum.Modules.Booking.Domain.Enums;

public enum PropertyStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Suspended
}

public enum BookingStatus
{
    PendingPayment,
    Confirmed,
    CheckedIn,
    Completed,
    Cancelled,
    Expired
}

public enum HostApplicationStatus
{
    Pending,
    Approved,
    Rejected
}
