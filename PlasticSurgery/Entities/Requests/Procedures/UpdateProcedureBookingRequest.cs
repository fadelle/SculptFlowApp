namespace PlasticSurgery.Entities.Requests.Procedures;

public record UpdateProcedureBookingRequest(
    string? Status,
    decimal? QuotedAmount,
    decimal? DepositAmount,
    decimal? FinalAmount,
    string? CurrencyCode,
    DateTimeOffset? ProcedureDate,
    string? Notes
);
