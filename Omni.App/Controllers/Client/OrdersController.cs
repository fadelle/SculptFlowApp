using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Orders;
using Omni.App.Entities.Requests.Orders;
using Omni.App.Entities.Responses.Orders;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/orders")]
public class OrdersController : DashboardApiController
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders, ICurrentClientContext clientContext) : base(clientContext)
    {
        _orders = orders;
    }

    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var order = await _orders.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(List), null, order);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Update(Guid id, [FromBody] UpdateOrderRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var order = await _orders.UpdateAsync(clientId.Value, id, request, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _orders.ListAsync(clientId.Value, status, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
