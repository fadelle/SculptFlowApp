using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Orders;

public interface IOrderRepository
{
    /// <summary>Tracked.</summary>
    Task<Order?> GetAsync(Guid clientId, Guid orderId, CancellationToken ct = default);

    /// <summary>Newest first, with Contact and Service loaded.</summary>
    Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(Guid clientId, string? status, int skip, int take,
        CancellationToken ct = default);

    void Add(Order order);

    /// <summary>Loads order.Contact and order.Service.</summary>
    Task LoadDetailsAsync(Order order, CancellationToken ct = default);
}
