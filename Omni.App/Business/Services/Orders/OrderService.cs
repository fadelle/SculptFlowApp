using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.Orders;
using Omni.App.Business.Mappers.Orders;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Orders;
using Omni.App.Entities.Responses.Orders;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Leads;
using Omni.App.Persistence.Contracts.Orders;

namespace Omni.App.Business.Services.Orders;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly ILeadRepository _leads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly ICatalogService _services;

    public OrderService(IOrderRepository orders, ILeadRepository leads, IUnitOfWork unitOfWork,
        IEventLogger events, ICatalogService services)
    {
        _orders = orders;
        _leads = leads;
        _unitOfWork = unitOfWork;
        _events = events;
        _services = services;
    }

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // A new order may only reference one of this client's ACTIVE services.
        await _services.EnsureUsableAsync(request.ClientId, request.ServiceId, ct);
        if (!await _leads.ExistsAsync(request.ClientId, request.LeadId, ct))
        {
            throw new ArgumentException("Lead not found for this client.");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            LeadId = request.LeadId,
            ServiceId = request.ServiceId,
            BookingId = request.BookingId,
            Status = OrderStatus.Considering,
            QuotedAmount = request.QuotedAmount,
            CurrencyCode = request.CurrencyCode,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _orders.Add(order);
        await _unitOfWork.SaveChangesAsync(ct);

        return await ToResponseAsync(order, ct);
    }

    public async Task<OrderResponse?> UpdateAsync(Guid clientId, Guid id, UpdateOrderRequest request, CancellationToken ct = default)
    {
        var order = await _orders.GetAsync(clientId, id, ct);
        if (order is null) return null;

        var previousStatus = order.Status;
        if (request.Status is not null)
        {
            if (!OrderStatus.All.Contains(request.Status))
            {
                throw new ArgumentException($"Invalid order status '{request.Status}'.", nameof(request));
            }
            order.Status = request.Status;
        }

        if (request.QuotedAmount is not null) order.QuotedAmount = request.QuotedAmount;
        if (request.DepositAmount is not null) order.DepositAmount = request.DepositAmount;
        if (request.FinalAmount is not null) order.FinalAmount = request.FinalAmount;
        if (request.CurrencyCode is not null) order.CurrencyCode = request.CurrencyCode;
        if (request.ServiceDate is not null) order.ServiceDate = request.ServiceDate;
        if (request.Notes is not null) order.Notes = request.Notes;

        order.UpdatedAt = DateTimeOffset.UtcNow;

        // The order counts from the moment it is confirmed (confirmed, deposit paid or completed), once: moving on from
        // confirmed to completed must not log a second order_confirmed event.
        if (IsConfirmed(request.Status) && !IsConfirmed(previousStatus))
        {
            var lead = await _leads.GetByIdAsync(order.LeadId, ct);
            if (lead is not null)
            {
                lead.Status = LeadStatus.SurgeryBooked;
                lead.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _events.Log(clientId, EventTypes.OrderConfirmed, leadId: order.LeadId,
                metadataJson: $"{{\"amount\": {order.FinalAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}}}");
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(order, ct);
    }

    private static bool IsConfirmed(string? status) =>
        status is OrderStatus.Confirmed or OrderStatus.DepositPaid or OrderStatus.Completed;

    public async Task<(IReadOnlyList<OrderResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _orders.ListAsync(clientId, status, skip, take, ct);
        return (items.Select(OrderMapper.ToResponse).ToList(), totalCount);
    }

    private async Task<OrderResponse> ToResponseAsync(Order o, CancellationToken ct)
    {
        await _orders.LoadDetailsAsync(o, ct);
        return OrderMapper.ToResponse(o);
    }
}
