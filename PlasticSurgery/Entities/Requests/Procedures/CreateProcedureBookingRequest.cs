namespace PlasticSurgery.Entities.Requests.Procedures;

public record CreateProcedureBookingRequest(
    Guid ClinicId,
    Guid LeadId,
    Guid ProcedureId,
    Guid? AppointmentId,
    decimal? QuotedAmount,
    string? CurrencyCode,
    string? Notes
);
