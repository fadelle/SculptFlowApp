namespace PlasticSurgery.Entities.Responses.Appointments;

public record UpcomingAppointmentsResponse(string Timezone, IReadOnlyList<UpcomingAppointmentResponse> Appointments);
