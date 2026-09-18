namespace PlasticSurgery.Dtos;

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

public record CreateProcedureBookingRequest(
    Guid ClinicId,
    Guid LeadId,
    Guid ProcedureId,
    Guid? AppointmentId,
    decimal? QuotedAmount,
    string? CurrencyCode,
    string? Notes
);

public record UpdateProcedureBookingRequest(
    string? Status,
    decimal? QuotedAmount,
    decimal? DepositAmount,
    decimal? FinalAmount,
    string? CurrencyCode,
    DateTimeOffset? ProcedureDate,
    string? Notes
);
