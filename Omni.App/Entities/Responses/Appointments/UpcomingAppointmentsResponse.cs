namespace Omni.App.Entities.Responses.Appointments;

public record UpcomingAppointmentsResponse(string Timezone, IReadOnlyList<UpcomingAppointmentResponse> Appointments);
