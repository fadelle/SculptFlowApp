namespace PlasticSurgery.Entities.Responses.Procedures;

public record ProcedureBookingResponse(
    Guid Id,
    Guid ClinicId,
    Guid LeadId,
    string? LeadFullName,
    Guid ProcedureId,
    string? ProcedureName,
    Guid? AppointmentId,
    string Status,
    decimal? QuotedAmount,
    decimal? DepositAmount,
    decimal? FinalAmount,
    string? CurrencyCode,
    DateTimeOffset? ProcedureDate,
    DateTimeOffset CreatedAt
);
