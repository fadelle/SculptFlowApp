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
using Omni.App.Persistence.Contracts.Bookings;
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
    private readonly IBookingRepository _bookings;

    public OrderService(IOrderRepository orders, IContactRepository contacts, IUnitOfWork unitOfWork,
        IEventLogger events, ICatalogService services, IBusinessTypeService businessTypes, IBookingRepository bookings)
    {
        _bookings = bookings;
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
        // The booking it follows must be this client's and this contact's. Another client's id and an unknown one get the
        // same answer, so booking ids can't be probed (QA t1-accounts-01; an unknown id used to break the foreign key: 500).
        if (request.BookingId is { } bookingId && await _bookings.GetForContactAsync(request.ClientId, request.ContactId, bookingId, ct) is null)
        {
            throw new ArgumentException("orders.errors.bookingNotFound");
        }
        // QA t3-inbox-09: amounts 0..10M and a real currency (a negative amount and "xyz" were saved; 1e15 and "DOLLR" gave 500s).
        var quoted = CheckAmount(request.QuotedAmount);
        var currency = CheckCurrency(request.CurrencyCode);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ContactId = request.ContactId,
            ServiceId = request.ServiceId,
            BookingId = request.BookingId,
            Status = OrderStatus.Considering,
            QuotedAmount = quoted,
            CurrencyCode = currency,
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
            // Only the moves the transition table allows (QA t3-inbox-09: a completed order could become canceled, a lost one
            // completed). Saving the same status again is fine (the popup sends it with every amount change).
            if (request.Status != previousStatus && !CanMove(previousStatus, request.Status))
            {
                throw new InvalidOperationException("orders.errors.statusChange");
            }
            order.Status = request.Status;
        }

        // Every value is checked before any is applied, so a bad one leaves the order as it was.
        var quoted = CheckAmount(request.QuotedAmount);
        var deposit = CheckAmount(request.DepositAmount);
        var final = CheckAmount(request.FinalAmount);
        var currency = request.CurrencyCode is null ? null : CheckCurrency(request.CurrencyCode);
        if (quoted is not null) order.QuotedAmount = quoted;
        if (deposit is not null) order.DepositAmount = deposit;
        if (final is not null) order.FinalAmount = final;
        if (currency is not null) order.CurrencyCode = currency;
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

        // A confirmed order that is canceled or lost no longer makes the contact a customer: when it was their only one, they
        // move to the pipeline's closed/lost stage (QA t3-inbox-09: they stayed "Customer").
        if (request.Status is OrderStatus.Canceled or OrderStatus.Lost && IsConfirmed(previousStatus))
        {
            await LeaveConvertedAsync(clientId, order, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await ToResponseAsync(order, ct);
    }

    private async Task LeaveConvertedAsync(Guid clientId, Order order, CancellationToken ct)
    {
        var contact = await _contacts.GetByIdAsync(order.ContactId, ct);
        if (contact is null) return;
        var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);
        if (pipeline.CategoryOf(contact.StageKey) != StageCategory.Converted) return;   // someone already moved them on
        var others = await _orders.ListForContactReadOnlyAsync(clientId, order.ContactId, ct);
        if (others.Any(o => o.Id != order.Id && IsConfirmed(o.Status))) return;         // another order still counts
        var stage = pipeline.MoveTo(contact.StageKey, StageCategory.ClosedLost);
        if (stage is null) return;
        contact.StageKey = stage;
        contact.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Which status may follow which. Open orders (considering, quoted) may go anywhere, steps may be skipped; once
    /// paid or confirmed an order can't go back to a quote; completed is final; canceled and lost can only be reopened.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        [OrderStatus.Considering] = [OrderStatus.Quoted, OrderStatus.DepositPaid, OrderStatus.Confirmed, OrderStatus.Completed, OrderStatus.Canceled, OrderStatus.Lost],
        [OrderStatus.Quoted] = [OrderStatus.Considering, OrderStatus.DepositPaid, OrderStatus.Confirmed, OrderStatus.Completed, OrderStatus.Canceled, OrderStatus.Lost],
        [OrderStatus.DepositPaid] = [OrderStatus.Confirmed, OrderStatus.Completed, OrderStatus.Canceled, OrderStatus.Lost],
        [OrderStatus.Confirmed] = [OrderStatus.DepositPaid, OrderStatus.Completed, OrderStatus.Canceled, OrderStatus.Lost],
        [OrderStatus.Completed] = [],
        [OrderStatus.Canceled] = [OrderStatus.Considering, OrderStatus.Quoted],
        [OrderStatus.Lost] = [OrderStatus.Considering, OrderStatus.Quoted],
    };

    /// <summary>True when an order in <paramref name="from"/> may move to <paramref name="to"/> (a different status).</summary>
    public static bool CanMove(string from, string to) => Transitions.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>The statuses an order in <paramref name="from"/> may move to (the contact page offers only these).</summary>
    public static IReadOnlyList<string> NextStatuses(string from) => Transitions.TryGetValue(from, out var next) ? next : [];

    /// <summary>Order amounts: 0 to 10,000,000, rounded to cents (the column is numeric(12,2)).</summary>
    public const decimal MaxAmount = 10_000_000m;

    private static decimal? CheckAmount(decimal? amount)
    {
        if (amount is null) return null;
        if (amount < 0 || amount > MaxAmount) throw new ArgumentException("orders.errors.amountRange");
        return Math.Round(amount.Value, 2);
    }

    /// <summary>Null stays null (no currency yet); anything typed must be an ISO 4217 code.</summary>
    private static string? CheckCurrency(string? currency)
    {
        if (currency is null) return null;
        return CurrencyCodes.Normalize(currency) ?? throw new ArgumentException("orders.errors.invalidCurrency");
    }

    private static bool IsConfirmed(string? status) =>
        status is OrderStatus.Confirmed or OrderStatus.DepositPaid or OrderStatus.Completed;

    public async Task<(IReadOnlyList<OrderResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? status, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _orders.ListAsync(clientId, status, skip, take, ct);
        return (items.Select(OrderMapper.ToResponse).ToList(), totalCount);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListForContactAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        (await _orders.ListForContactReadOnlyAsync(clientId, contactId, ct)).Select(OrderMapper.ToResponse).ToList();

    private async Task<OrderResponse> ToResponseAsync(Order o, CancellationToken ct)
    {
        await _orders.LoadDetailsAsync(o, ct);
        return OrderMapper.ToResponse(o);
    }
}
