namespace Omni.App.Entities.Responses.Orders;

public record OrderResponse(
    Guid Id,
    Guid ClientId,
    Guid ContactId,
    string? ContactFullName,
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
