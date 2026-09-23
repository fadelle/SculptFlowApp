namespace PlasticSurgery.Dtos;

/// <summary>GET /api/ai/appointments/available (the AI's get_available_slots tool) and GET /api/appointments/available.
/// Slot Start/End carry the clinic's local UTC offset (e.g. 2026-09-24T09:00:00+03:00), so they are unambiguous and can be
/// passed straight back to book_consultation.</summary>
public record AvailabilityResponse(
    /// <summary>False until the clinic has at least one open weekday configured — then Slots is always empty and Message says why.</summary>
    bool Configured,
    string Timezone,
    /// <summary>Today's date in the clinic's timezone ("yyyy-MM-dd") — lets the AI resolve "tomorrow", "Friday" etc. from the
    /// tool result itself instead of guessing the current date.</summary>
    string Today,
    int DurationMinutes,
    /// <summary>First/last local date searched ("yyyy-MM-dd"), after clamping to today and the booking horizon.</summary>
    string From,
    string To,
    string? Message,
    IReadOnlyList<AvailableSlotResponse> Slots
)
{
    public string TodayDayOfWeek => DateOnly.Parse(Today).DayOfWeek.ToString();
    public string Tomorrow => DateOnly.Parse(Today).AddDays(1).ToString("yyyy-MM-dd");
    public string TomorrowDayOfWeek => DateOnly.Parse(Today).AddDays(1).DayOfWeek.ToString();
}

/// <summary>Start/End are the exact values to pass back to book_consultation. Date/Time/Label are the same moment as plain
/// clinic-local text (Time is 24-hour "HH:mm", Label is what to say to the patient) so the AI never has to read an offset.</summary>
public record AvailableSlotResponse(DateTimeOffset Start, DateTimeOffset End, string Date, string Time, string Label);

/// <summary>Weekly window as staff edit it. DayOfWeek uses .NET numbering (0 = Sunday). Times are "HH:mm" clinic-local.</summary>
public record DayRuleDto(int DayOfWeek, bool IsOpen, string Start, string End);

public record BookingRulesDto(int DefaultDurationMinutes, int BufferMinutes, int MinimumNoticeMinutes, int MaxAdvanceDays);

public record AvailabilityExceptionDto(Guid Id, string Date, bool IsClosed, string? Start, string? End, string? Reason);

public record AvailabilitySettingsDto(
    string Timezone,
    bool Configured,
    IReadOnlyList<DayRuleDto> Days,
    BookingRulesDto Booking,
    IReadOnlyList<AvailabilityExceptionDto> Exceptions
);
