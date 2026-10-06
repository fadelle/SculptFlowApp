namespace PlasticSurgery.Entities.Dtos.Appointments;

public record AvailabilitySettingsDto(
    string Timezone,
    bool Configured,
    IReadOnlyList<DayRuleDto> Days,
    BookingRulesDto Booking,
    IReadOnlyList<AvailabilityExceptionDto> Exceptions
);
