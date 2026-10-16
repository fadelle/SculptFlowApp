namespace Omni.App.Entities.Dtos.Bookings;

public record BookingRulesDto(int DefaultDurationMinutes, int BufferMinutes, int MinimumNoticeMinutes, int MaxAdvanceDays);
