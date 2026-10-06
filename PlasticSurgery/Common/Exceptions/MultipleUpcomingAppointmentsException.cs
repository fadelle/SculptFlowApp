using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>The lead has more than one upcoming appointment and no appointmentId was given, so reschedule/cancel can't tell which one to act on.
/// Appointments lists them (with ids) so the AI can ask the patient and retry with appointmentId.</summary>
public class MultipleUpcomingAppointmentsException : Exception
{
    public MultipleUpcomingAppointmentsException(IReadOnlyList<UpcomingAppointmentResponse> appointments)
        : base("This patient has more than one upcoming appointment. Ask which one, then repeat the request with its appointmentId.")
    {
        Appointments = appointments;
    }

    public IReadOnlyList<UpcomingAppointmentResponse> Appointments { get; }
}
