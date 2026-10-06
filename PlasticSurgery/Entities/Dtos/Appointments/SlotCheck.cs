namespace PlasticSurgery.Entities.Dtos.Appointments;

/// <summary>Result of <see cref="IAvailabilityService.CheckSlotAsync"/>: Error is null when the slot is bookable, and End is
/// the interval end actually checked (the requested end, or start + the procedure/default duration).</summary>
public record SlotCheck(string? Error, DateTimeOffset End);
