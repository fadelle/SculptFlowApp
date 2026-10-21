namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>A booking whose reminder of one kind is due and has no row yet (IBookingReminderRepository.ListDueAsync).</summary>
public record DueReminder(Guid BookingId, Guid ContactId, DateTimeOffset ScheduledStart, DateTimeOffset BookingCreatedAt, string? ServiceName);
