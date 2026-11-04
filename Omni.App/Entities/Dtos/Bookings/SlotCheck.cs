namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>Result of <see cref="IAvailabilityService.CheckSlotAsync"/>: Error is null when the slot is bookable, and End is
/// the interval end actually checked (the requested end, or start + the service/default duration). Reason is a
/// <c>SlotCheckReason</c> when refused.</summary>
public record SlotCheck(string? Error, DateTimeOffset End, string? Reason = null);
