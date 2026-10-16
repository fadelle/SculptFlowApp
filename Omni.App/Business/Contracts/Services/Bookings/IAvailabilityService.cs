using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Business.Contracts.Services.Bookings;

/// <summary>Client-level booking availability: the weekly schedule, date exceptions and booking rules staff configure
/// under Business Info → Availability, turned into real bookable slots. This — not the free-text Operating Hours — is the
/// booking source of truth. Everything is client-scoped and computed in the client's own timezone (Client.Timezone).</summary>
public interface IAvailabilityService
{
    /// <summary>Open slots for the local dates [from, from + days - 1] (default from = today in the client's timezone),
    /// after weekly schedule + exceptions + duration + buffer + minimum notice + booking horizon + existing bookings.
    /// A client with no open weekday configured gets Configured = false and no slots — never "open 24/7".
    /// Throws ArgumentException for an unknown or inactive service.</summary>
    Task<AvailabilityResponse> GetSlotsAsync(Guid clientId, Guid? serviceId, DateOnly? from, int days, CancellationToken ct = default);

    /// <summary>The final booking check: is [start, end) still bookable right now? Error is null when yes, otherwise a
    /// human-readable reason. Same rules as slot generation, but validates the requested interval instead of a grid.
    /// excludeBookingId ignores one booking (the one being rescheduled) so it does not block its own new time.</summary>
    Task<SlotCheck> CheckSlotAsync(Guid clientId, Guid? serviceId, DateTimeOffset start, DateTimeOffset? end, Guid? excludeBookingId = null, CancellationToken ct = default);

    Task<AvailabilitySettingsDto> GetSettingsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Saves the timezone, the seven weekly rules and the booking rules together. ArgumentException on invalid input.</summary>
    Task SaveScheduleAsync(Guid clientId, string timezone, IReadOnlyList<DayRuleDto> days, BookingRulesDto booking, CancellationToken ct = default);

    /// <summary>Adds or replaces the exception for a date (closed all day, or a custom window).</summary>
    Task SaveExceptionAsync(Guid clientId, DateOnly date, bool isClosed, string? start, string? end, string? reason, CancellationToken ct = default);

    Task DeleteExceptionAsync(Guid clientId, Guid exceptionId, CancellationToken ct = default);
}
