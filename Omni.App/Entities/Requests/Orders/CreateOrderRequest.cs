namespace Omni.App.Entities.Requests.Orders;

public record CreateOrderRequest(
    Guid ClientId,
    Guid LeadId,
    Guid ServiceId,
    Guid? AppointmentId,
    decimal? QuotedAmount,
    string? CurrencyCode,
    string? Notes
);
