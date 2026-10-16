namespace Omni.App.Entities.Responses.Bookings;

/// <summary>Start/End are the exact values to pass back to book_slot. Date/Time/Label are the same moment as plain
/// client-local text (Time is 24-hour "HH:mm", Label is what to say to the contact) so the AI never has to read an offset.</summary>
public record AvailableSlotResponse(DateTimeOffset Start, DateTimeOffset End, string Date, string Time, string Label);
