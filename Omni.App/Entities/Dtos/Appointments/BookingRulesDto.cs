namespace Omni.App.Entities.Dtos.Appointments;

public record BookingRulesDto(int DefaultDurationMinutes, int BufferMinutes, int MinimumNoticeMinutes, int MaxAdvanceDays);
