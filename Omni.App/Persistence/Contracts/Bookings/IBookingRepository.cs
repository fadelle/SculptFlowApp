using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Bookings;

/// <summary>Bookings. Returned entities are tracked unless the method says read-only.</summary>
public interface IBookingRepository
{
    void Add(Booking booking);

    Task<Booking?> GetAsync(Guid clientId, Guid bookingId, CancellationToken ct = default);

    /// <summary>With Lead and Service loaded.</summary>
    Task<Booking?> GetWithDetailsAsync(Guid clientId, Guid bookingId, CancellationToken ct = default);

    Task<Booking?> GetForLeadAsync(Guid clientId, Guid leadId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Read-only, with Service loaded.</summary>
    Task<Booking?> GetWithServiceReadOnlyAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Read-only, with Service: the lead's booked/confirmed bookings starting after <paramref name="now"/>, soonest first.</summary>
    Task<IReadOnlyList<Booking>> ListUpcomingForLeadAsync(Guid clientId, Guid leadId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>With Lead and Service, ordered by start time.</summary>
    Task<(IReadOnlyList<Booking> Items, int TotalCount)> ListAsync(Guid clientId, string? status, DateTimeOffset? from,
        DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    /// <summary>With Lead and Service: bookings starting in [fromUtc, toUtc), ordered by start time.</summary>
    Task<IReadOnlyList<Booking>> ListStartingBetweenAsync(Guid clientId, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken ct = default);

    /// <summary>Past bookings still booked/confirmed (no outcome recorded yet).</summary>
    Task<int> CountNeedingOutcomeAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Bookings that block slots (not canceled/rescheduled) starting in [lowUtc, highUtc).</summary>
    Task<IReadOnlyList<BusyBookingRow>> ListBusyAsync(Guid clientId, DateTimeOffset lowUtc, DateTimeOffset highUtc,
        Guid? excludeBookingId, CancellationToken ct = default);

    /// <summary>Loads Lead, and Service when set.</summary>
    Task LoadDetailsAsync(Booking booking, CancellationToken ct = default);

    /// <summary>Serializes bookings per client until the surrounding transaction ends (Postgres advisory lock).</summary>
    Task LockClientForBookingAsync(Guid clientId, CancellationToken ct = default);
}
