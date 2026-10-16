using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Orders;

namespace Omni.App.Persistence.Repositories.Orders;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _db;

    public OrderRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<Order?> GetAsync(Guid clientId, Guid orderId, CancellationToken ct = default) =>
        _db.Orders.FirstOrDefaultAsync(o => o.ClientId == clientId && o.Id == orderId, ct);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(Guid clientId, string? status,
        int skip, int take, CancellationToken ct = default)
    {
        var query = _db.Orders
            .Include(o => o.Contact)
            .Include(o => o.Service)
            .Where(o => o.ClientId == clientId);

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public void Add(Order order) => _db.Orders.Add(order);

    public async Task LoadDetailsAsync(Order order, CancellationToken ct = default)
    {
        await _db.Entry(order).Reference(x => x.Contact).LoadAsync(ct);
        await _db.Entry(order).Reference(x => x.Service).LoadAsync(ct);
    }
}
