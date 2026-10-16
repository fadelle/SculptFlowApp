using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Orders;

namespace Omni.App.Business.Mappers.Orders;

public static class OrderMapper
{
    /// <summary>Expects order.Contact and order.Service to be loaded.</summary>
    public static OrderResponse ToResponse(Order o) => new(
        o.Id, o.ClientId, o.ContactId, o.Contact?.FullName, o.ServiceId, o.Service?.Name, o.BookingId,
        o.Status, o.QuotedAmount, o.DepositAmount, o.FinalAmount, o.CurrencyCode, o.ServiceDate, o.CreatedAt
    );
}
