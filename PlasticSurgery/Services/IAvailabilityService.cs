using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>Clinic-level consultation availability: the weekly schedule, date exceptions and booking rules staff configure
/// under Clinic Info → Availability, turned into real bookable slots. This — not the free-text Operating Hours — is the
/// booking source of truth. Everything is clinic-scoped and computed in the clinic's own timezone (Clinic.Timezone).</summary>
public interface IAvailabilityService
{
    /// <summary>Open slots for the local dates [from, from + days - 1] (default from = today in the clinic's timezone),
    /// after weekly schedule + exceptions + duration + buffer + minimum notice + booking horizon + existing appointments.
    /// A clinic with no open weekday configured gets Configured = false and no slots — never "open 24/7".
    /// Throws ArgumentException for an unknown or inactive procedure.</summary>
    Task<AvailabilityResponse> GetSlotsAsync(Guid clinicId, Guid? procedureId, DateOnly? from, int days, CancellationToken ct = default);

    /// <summary>The final booking check: is [start, end) still bookable right now? Error is null when yes, otherwise a
    /// human-readable reason. Same rules as slot generation, but validates the requested interval instead of a grid.</summary>
    Task<SlotCheck> CheckSlotAsync(Guid clinicId, Guid? procedureId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct = default);

    Task<AvailabilitySettingsDto> GetSettingsAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Saves the timezone, the seven weekly rules and the booking rules together. ArgumentException on invalid input.</summary>
    Task SaveScheduleAsync(Guid clinicId, string timezone, IReadOnlyList<DayRuleDto> days, BookingRulesDto booking, CancellationToken ct = default);

    /// <summary>Adds or replaces the exception for a date (closed all day, or a custom window).</summary>
    Task SaveExceptionAsync(Guid clinicId, DateOnly date, bool isClosed, string? start, string? end, string? reason, CancellationToken ct = default);

    Task DeleteExceptionAsync(Guid clinicId, Guid exceptionId, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IAvailabilityService.CheckSlotAsync"/>: Error is null when the slot is bookable, and End is
/// the interval end actually checked (the requested end, or start + the procedure/default duration).</summary>
public record SlotCheck(string? Error, DateTimeOffset End);
