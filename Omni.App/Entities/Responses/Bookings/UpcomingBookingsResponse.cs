namespace Omni.App.Entities.Responses.Bookings;

public record UpcomingBookingsResponse(string Timezone, IReadOnlyList<UpcomingBookingResponse> Bookings);
