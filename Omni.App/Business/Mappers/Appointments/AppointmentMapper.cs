using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Business.Mappers.Appointments;

public static class AppointmentMapper
{
    /// <summary>Expects Lead (and Procedure, when set) to be loaded.</summary>
    public static AppointmentResponse ToResponse(Appointment a) => new(
        a.Id, a.ClinicId, a.LeadId, a.Lead?.FullName, a.ProcedureId, a.Procedure?.Name,
        a.AppointmentType, a.Status, a.ScheduledStart, a.ScheduledEnd,
        a.LocationType, a.LocationName, a.Notes, a.CreatedAt
    );
}
