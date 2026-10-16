namespace Omni.App.Entities.Requests.Procedures;

public record CreateProcedureBookingRequest(
    Guid ClientId,
    Guid LeadId,
    Guid ProcedureId,
    Guid? AppointmentId,
    decimal? QuotedAmount,
    string? CurrencyCode,
    string? Notes
);
