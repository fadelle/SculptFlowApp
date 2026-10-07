namespace PlasticSurgery.Entities.Responses.Appointments;

/// <summary>Start/End are the exact values to pass back to book_consultation. Date/Time/Label are the same moment as plain
/// clinic-local text (Time is 24-hour "HH:mm", Label is what to say to the patient) so the AI never has to read an offset.</summary>
public record AvailableSlotResponse(DateTimeOffset Start, DateTimeOffset End, string Date, string Time, string Label);
