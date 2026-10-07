using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Entities.Dtos.Appointments;

/// <summary>What the backend knows about a patient's bookings — the single definition of "upcoming appointment" shared by
/// get_my_appointments, the book_consultation guard and get_available_slots. CanCreateNewBooking is the explicit decision.</summary>
public record PatientBookingContext(
    bool HasUpcomingAppointment,
    bool CanCreateNewBooking,
    string? BookingBlockReason,
    IReadOnlyList<UpcomingAppointmentResponse> ExistingUpcomingAppointments
);
