using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Orders;

namespace Omni.App.Business.Mappers.Orders;

public static class OrderMapper
{
    /// <summary>Expects order.Lead and order.Service to be loaded.</summary>
    public static OrderResponse ToResponse(Order o) => new(
        o.Id, o.ClientId, o.LeadId, o.Lead?.FullName, o.ServiceId, o.Service?.Name, o.AppointmentId,
        o.Status, o.QuotedAmount, o.DepositAmount, o.FinalAmount, o.CurrencyCode, o.ServiceDate, o.CreatedAt
    );
}
