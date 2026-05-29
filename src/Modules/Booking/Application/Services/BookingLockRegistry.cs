using System.Collections.Concurrent;

namespace Nexum.Modules.Booking.Application.Services;

/// <summary>
/// Code-level pessimistic locking for booking operations.
///
/// Uses a ConcurrentDictionary of SemaphoreSlim instances keyed on
/// "{roomTypeId}:{sortedDates}" to ensure only one booking operation
/// per room+date combination executes at a time.
///
/// WaitAsync(0) = fail immediately if locked (no queuing).
/// This prevents backlog buildup under peak HGC concurrency.
///
/// Registered as Singleton — one registry for the entire app lifetime.
/// SemaphoreSlim itself is NOT thread-safe for disposal, but since we
/// never remove entries the instances live forever — correct for this use.
/// </summary>
public sealed class BookingLockRegistry
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetLock(Guid roomTypeId, IEnumerable<DateOnly> dates)
    {
        var sortedDates = string.Join(',', dates.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd")));
        var key = $"{roomTypeId}:{sortedDates}";
        return _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }
}
