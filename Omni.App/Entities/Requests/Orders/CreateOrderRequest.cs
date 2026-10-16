namespace Omni.App.Entities.Requests.Orders;

public record CreateOrderRequest(
    Guid ClientId,
    Guid ContactId,
    Guid ServiceId,
    Guid? BookingId,
    decimal? QuotedAmount,
    string? CurrencyCode,
    string? Notes
);
