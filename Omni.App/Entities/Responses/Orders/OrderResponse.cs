namespace Omni.App.Entities.Responses.Orders;

public record OrderResponse(
    Guid Id,
    Guid ClientId,
    Guid LeadId,
    string? LeadFullName,
    Guid ServiceId,
    string? ServiceName,
    Guid? BookingId,
    string Status,
    decimal? QuotedAmount,
    decimal? DepositAmount,
    decimal? FinalAmount,
    string? CurrencyCode,
    DateTimeOffset? ServiceDate,
    DateTimeOffset CreatedAt
);
