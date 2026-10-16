namespace Omni.App.Entities.Requests.Orders;

public record UpdateOrderRequest(
    string? Status,
    decimal? QuotedAmount,
    decimal? DepositAmount,
    decimal? FinalAmount,
    string? CurrencyCode,
    DateTimeOffset? ServiceDate,
    string? Notes
);
