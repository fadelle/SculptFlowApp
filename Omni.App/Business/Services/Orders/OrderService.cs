using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.Orders;
using Omni.App.Business.Mappers.Orders;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Orders;
using Omni.App.Entities.Responses.Orders;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Contacts;
using Omni.App.Persistence.Contracts.Orders;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.Orders;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly IContactRepository _contacts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly ICatalogService _services;
    private readonly IBusinessTypeService _businessTypes;

    public OrderService(IOrderRepository orders, IContactRepository contacts, IUnitOfWork unitOfWork,
        IEventLogger events, ICatalogService services, IBusinessTypeService businessTypes)
    {
        _orders = orders;
        _contacts = contacts;
        _unitOfWork = unitOfWork;
        _events = events;
        _services = services;
        _businessTypes = businessTypes;
    }

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        // A new order may only reference one of this client's ACTIVE services.
        await _services.EnsureUsableAsync(request.ClientId, request.ServiceId, ct);
        if (!await _contacts.ExistsAsync(request.ClientId, request.ContactId, ct))
        {
            throw new ArgumentException("contacts.errors.notFound");
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ContactId = request.ContactId,
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
                throw new ArgumentException("orders.errors.invalidStatus").WithArgs(request.Status);
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
            var contact = await _contacts.GetByIdAsync(order.ContactId, ct);
            if (contact is not null)
            {
                var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);
                contact.StageKey = pipeline.MoveTo(contact.StageKey, StageCategory.Converted) ?? contact.StageKey;
                contact.UpdatedAt = DateTimeOffset.UtcNow;
            }

            _events.Log(clientId, EventTypes.OrderConfirmed, contactId: order.ContactId,
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
