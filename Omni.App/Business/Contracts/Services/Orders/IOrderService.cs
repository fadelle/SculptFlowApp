using Omni.App.Entities.Requests.Orders;
using Omni.App.Entities.Responses.Orders;

namespace Omni.App.Business.Contracts.Services.Orders;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);

    Task<OrderResponse?> UpdateAsync(Guid clientId, Guid id, UpdateOrderRequest request, CancellationToken ct = default);

    Task<(IReadOnlyList<OrderResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, int skip, int take, CancellationToken ct = default);
}
