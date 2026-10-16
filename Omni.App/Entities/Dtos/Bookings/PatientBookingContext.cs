using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>What the backend knows about a patient's bookings — the single definition of "upcoming booking" shared by
/// get_my_bookings, the book_slot guard and get_available_slots. CanCreateNewBooking is the explicit decision.</summary>
public record PatientBookingContext(
    bool HasUpcomingBooking,
    bool CanCreateNewBooking,
    string? BookingBlockReason,
    IReadOnlyList<UpcomingBookingResponse> ExistingUpcomingBookings
);
