using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>The lead already has an upcoming booked/confirmed appointment; book_consultation refuses to make a second one.
/// Existing describes it so the AI can offer to move it instead.</summary>
public class LeadAlreadyBookedException : Exception
{
    public LeadAlreadyBookedException(UpcomingAppointmentResponse existing)
        : base($"This patient already has an upcoming appointment ({existing.Label}). Reschedule or cancel it instead of booking another.")
    {
        Existing = existing;
    }

    public UpcomingAppointmentResponse Existing { get; }
}
