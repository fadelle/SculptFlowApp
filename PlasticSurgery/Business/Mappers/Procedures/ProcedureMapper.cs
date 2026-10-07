using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Procedures;

namespace PlasticSurgery.Business.Mappers.Procedures;

public static class ProcedureMapper
{
    public static ProcedureResponse ToResponse(Procedure p) =>
        new(p.Id, p.ClinicId, p.Name, p.Code, p.Description, p.ConsultationDuration, p.IsActive);

    /// <summary>Expects booking.Lead and booking.Procedure to be loaded.</summary>
    public static ProcedureBookingResponse ToResponse(ProcedureBooking b) => new(
        b.Id, b.ClinicId, b.LeadId, b.Lead?.FullName, b.ProcedureId, b.Procedure?.Name, b.AppointmentId,
        b.Status, b.QuotedAmount, b.DepositAmount, b.FinalAmount, b.CurrencyCode, b.ProcedureDate, b.CreatedAt
    );
}
