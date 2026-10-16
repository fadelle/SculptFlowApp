namespace Omni.App.Entities.Dtos.Bookings;

public record AvailabilitySettingsDto(
    string Timezone,
    bool Configured,
    IReadOnlyList<DayRuleDto> Days,
    BookingRulesDto Booking,
    IReadOnlyList<AvailabilityExceptionDto> Exceptions
);
