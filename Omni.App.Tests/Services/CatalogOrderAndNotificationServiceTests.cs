using Omni.App.Business.Services.Notifications;
using Omni.App.Business.Services.Catalog;
using Omni.App.Business.Services.Orders;

namespace Omni.App.Tests.Services;

public class ServiceServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IServiceRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private CatalogService Sut() => new(_repo.Object, _uow.Object);

    private Service Existing(bool active = true)
    {
        var p = new Service { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Rhinoplasty", IsActive = active };
        _repo.Setup(r => r.GetAsync(_clientId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _repo.Setup(r => r.GetReadOnlyAsync(_clientId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    [Fact]
    public async Task List_and_GetById_map()
    {
        var p = Existing();
        _repo.Setup(r => r.ListAsync(_clientId, true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Service> { p });
        Assert.Single(await Sut().ListAsync(_clientId, true));
        Assert.Equal(p.Id, (await Sut().GetByIdAsync(_clientId, p.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_trims_and_saves_an_active_service()
    {
        Service? added = null;
        _repo.Setup(r => r.Add(It.IsAny<Service>())).Callback<Service>(p => added = p);
        var r = await Sut().CreateAsync(new CreateServiceRequest(_clientId, "  Facelift ", " FL ", "  ", 45));
        Assert.Equal(("Facelift", "FL", null, 45, true), (added!.Name, added.Code, added.Description, added.BookingDuration, added.IsActive));
        Assert.Equal("Facelift", r.Name);
    }

    [Fact]
    public async Task A_service_can_have_a_price_with_its_currency_and_both_go_to_the_ai()
    {
        // QA t5-ops-18: services had no price; the AI could only quote the Knowledge Base text.
        Service? added = null;
        _repo.Setup(r => r.Add(It.IsAny<Service>())).Callback<Service>(p => added = p);
        var r = await Sut().CreateAsync(new CreateServiceRequest(_clientId, "Facial", null, null, 45, 49.999m, " usd "));
        Assert.Equal((50.00m, "USD"), (added!.Price, added.Currency));
        Assert.Equal((50.00m, "USD"), (r.Price, r.Currency));

        // A currency without a price is dropped; clearing the price clears both.
        var p = Existing();
        p.Price = 10m; p.Currency = "EUR";
        await Sut().UpdateAsync(_clientId, p.Id, new UpdateServiceRequest("Nose job", null, null, 30, null, "EUR"));
        Assert.Equal(((decimal?)null, (string?)null), (p.Price, p.Currency));
    }

    [Theory]
    [InlineData(-1, "USD", "services.errors.priceRange")]
    [InlineData(10_000_001, "USD", "services.errors.priceRange")]
    [InlineData(10, null, "services.errors.currencyRequired")]
    [InlineData(10, "US", "services.errors.currencyRequired")]
    [InlineData(10, "U5D", "services.errors.currencyRequired")]
    [InlineData(10, "XYZ", "services.errors.currencyRequired")] // three letters but not an ISO 4217 code (QA t3-inbox-09)
    public async Task A_price_must_be_sensible_and_name_its_currency(double price, string? currency, string key)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, "Facial", null, null, 45, (decimal)price, currency)));
        Assert.Equal(key, ex.Message);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_duplicates_in_the_same_client()
    {
        _repo.Setup(r => r.NameExistsAsync(_clientId, "Facelift", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, "Facelift", null, null, null)));
    }

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("   ", null, null, null)]
    [InlineData("ok", null, null, 0)]
    [InlineData("ok", null, null, 481)]
    public async Task Create_validates_inputs(string name, string? code, string? description, int? duration) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, name, code, description, duration)));

    [Fact]
    public async Task Create_validates_lengths()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, new string('n', 201), null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, "n", new string('c', 101), null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateServiceRequest(_clientId, "n", null, new string('d', 501), null)));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_changes_fields_and_excludes_itself_from_the_duplicate_check()
    {
        var p = Existing();
        var r = await Sut().UpdateAsync(_clientId, p.Id, new UpdateServiceRequest("Nose job", "NJ", "desc", 30));
        Assert.Equal(("Nose job", "NJ", "desc", 30), (p.Name, p.Code, p.Description, p.BookingDuration));
        _repo.Verify(x => x.NameExistsAsync(_clientId, "Nose job", p.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Nose job", r!.Name);
        Assert.Null(await Sut().UpdateAsync(_clientId, Guid.NewGuid(), new UpdateServiceRequest("x", null, null, null)));
        _repo.Setup(x => x.NameExistsAsync(_clientId, "Dup", p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, p.Id, new UpdateServiceRequest("Dup", null, null, null)));
    }

    [Fact]
    public async Task SetActive_toggles()
    {
        var p = Existing();
        Assert.False((await Sut().SetActiveAsync(_clientId, p.Id, false))!.IsActive);
        Assert.Null(await Sut().SetActiveAsync(_clientId, Guid.NewGuid(), true));
    }

    [Fact]
    public async Task EnsureUsable_accepts_only_active_services_of_the_client()
    {
        var active = Existing();
        await Sut().EnsureUsableAsync(_clientId, active.Id);
        var inactive = Existing(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().EnsureUsableAsync(_clientId, inactive.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().EnsureUsableAsync(_clientId, Guid.NewGuid()));
    }
}

public class OrderServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IBusinessTypeService> _businessTypes = PipelineKit.Types();
    private readonly Mock<IBookingRepository> _bookings = new();

    public OrderServiceTests() =>
        _contacts.Setup(l => l.ExistsAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private OrderService Sut() => new(_orders.Object, _contacts.Object, _uow.Object, _events.Object, _services.Object, _businessTypes.Object, _bookings.Object);

    [Fact]
    public async Task Create_refuses_a_booking_of_another_client_or_contact_and_an_unknown_one_alike()
    {
        // QA t1-accounts-01: another client's booking was linked silently and an unknown id gave a 500.
        var contactId = Guid.NewGuid();
        var foreign = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateOrderRequest(_clientId, contactId, Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", null)));
        Assert.Equal("orders.errors.bookingNotFound", foreign.Message);
        _orders.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);

        var booking = new Booking { Id = Guid.NewGuid(), ClientId = _clientId, ContactId = contactId };
        _bookings.Setup(b => b.GetForContactAsync(_clientId, contactId, booking.Id, It.IsAny<CancellationToken>())).ReturnsAsync(booking);
        Order? added = null;
        _orders.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(o => added = o);
        await Sut().CreateAsync(new CreateOrderRequest(_clientId, contactId, Guid.NewGuid(), booking.Id, 10m, "USD", null));
        Assert.Equal(booking.Id, added!.BookingId);
    }

    private Order MakeOrder(string status = OrderStatus.Considering, Guid? contactId = null)
    {
        var b = new Order { Id = Guid.NewGuid(), ClientId = _clientId, ContactId = contactId ?? Guid.NewGuid(), ServiceId = Guid.NewGuid(), Status = status };
        _orders.Setup(r => r.GetAsync(_clientId, b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        return b;
    }

    [Fact]
    public async Task Create_checks_the_service_and_starts_as_considering()
    {
        Order? added = null;
        _orders.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(b => added = b);
        var contactId = Guid.NewGuid();
        var procId = Guid.NewGuid();
        await Sut().CreateAsync(new CreateOrderRequest(_clientId, contactId, procId, null, 1200m, "USD", "n"));
        Assert.Equal((OrderStatus.Considering, 1200m, "USD", contactId), (added!.Status, added.QuotedAmount, added.CurrencyCode, added.ContactId));
        _services.Verify(p => p.EnsureUsableAsync(_clientId, procId, It.IsAny<CancellationToken>()), Times.Once);
        _orders.Verify(b => b.LoadDetailsAsync(added, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_with_an_unusable_service_saves_nothing()
    {
        _services.Setup(p => p.EnsureUsableAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("inactive"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null)));
        _orders.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_a_contact_from_another_client()
    {
        _contacts.Setup(l => l.ExistsAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null)));
    }

    [Fact]
    public async Task Update_applies_only_supplied_fields()
    {
        var b = MakeOrder();
        b.QuotedAmount = 100m;
        var date = DateTimeOffset.UtcNow.AddDays(30);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Quoted, null, 50m, 90m, "EUR", date, "note"));
        Assert.Equal((OrderStatus.Quoted, 100m, 50m, 90m, "EUR", date, "note"), (b.Status, b.QuotedAmount, b.DepositAmount, b.FinalAmount, b.CurrencyCode, b.ServiceDate, b.Notes));
        _events.Verify(e => e.Log(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Update_validates_status_and_missing_order()
    {
        Assert.Null(await Sut().UpdateAsync(_clientId, Guid.NewGuid(), new UpdateOrderRequest(null, null, null, null, null, null, null)));
        var b = MakeOrder();
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest("bogus", null, null, null, null, null, null)));
    }

    [Fact]
    public async Task Completing_an_order_marks_the_contact_and_logs_the_amount_invariantly()
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Attended };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(contactId: contact.Id);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Completed, null, null, 1234.5m, null, null, null));
        Assert.Equal(PipelineKit.Customer, contact.StageKey);
        _events.Verify(e => e.Log(_clientId, EventTypes.OrderConfirmed, contact.Id, null, null, null, "{\"amount\": 1234.5}"), Times.Once);
    }

    [Fact]
    public async Task Completing_without_a_final_amount_logs_null()
    {
        var b = MakeOrder();
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Completed, null, null, null, null, null, null));
        _events.Verify(e => e.Log(_clientId, EventTypes.OrderConfirmed, b.ContactId, null, null, null, "{\"amount\": null}"), Times.Once);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.DepositPaid)]
    public async Task Confirming_an_order_moves_the_contact_to_the_converted_stage(string status)
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Attended };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(contactId: contact.Id);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(status, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.Customer, contact.StageKey);
        _events.Verify(e => e.Log(_clientId, EventTypes.OrderConfirmed, contact.Id, null, null, null, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Confirming_follows_the_converted_category_of_the_clients_own_pipeline()
    {
        _businessTypes.Use(PipelineKit.Custom());
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = "visited" };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        await Sut().UpdateAsync(_clientId, MakeOrder(contactId: contact.Id).Id, new UpdateOrderRequest(OrderStatus.Confirmed, null, null, null, null, null, null));
        Assert.Equal("signed", contact.StageKey);
    }

    [Fact]
    public async Task Moving_from_confirmed_to_completed_does_not_log_a_second_order_confirmed_event()
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Customer };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(OrderStatus.Confirmed, contact.Id);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Completed, null, null, 900m, null, null, null));
        _events.Verify(e => e.Log(It.IsAny<Guid>(), EventTypes.OrderConfirmed, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Earlier_stages_leave_the_contact_alone()
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Attended };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(contactId: contact.Id);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Quoted, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.Attended, contact.StageKey);
    }

    // ---- QA t3-inbox-09: amounts, currency, status moves and the contact's stage after a cancel ----

    [Theory]
    [InlineData(-10)]
    [InlineData(10_000_000.01)]
    [InlineData(1e15)]
    public async Task Create_refuses_an_amount_outside_0_to_10_million(double amount)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, (decimal)amount, "USD", null)));
        Assert.Equal("orders.errors.amountRange", ex.Message);
        _orders.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("DOLLR")]
    [InlineData("")]
    [InlineData("U5D")]
    public async Task Create_refuses_a_currency_that_is_not_an_ISO_code(string currency)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, 10m, currency, null)));
        Assert.Equal("orders.errors.invalidCurrency", ex.Message);
        _orders.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task Create_upper_cases_the_currency_rounds_to_cents_and_allows_none()
    {
        var added = new List<Order>();
        _orders.Setup(r => r.Add(It.IsAny<Order>())).Callback<Order>(added.Add);
        await Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, 10.456m, " eur ", null));
        await Sut().CreateAsync(new CreateOrderRequest(_clientId, Guid.NewGuid(), Guid.NewGuid(), null, 0m, null, null));
        Assert.Equal(("EUR", 10.46m), (added[0].CurrencyCode, added[0].QuotedAmount));
        Assert.Equal(((string?)null, 0m), (added[1].CurrencyCode, added[1].QuotedAmount));
    }

    [Fact]
    public async Task Update_with_a_bad_value_changes_nothing()
    {
        var b = MakeOrder();
        b.QuotedAmount = 100m;
        b.CurrencyCode = "USD";
        var amount = await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(null, 200m, -5m, null, "EUR", null, null)));
        Assert.Equal("orders.errors.amountRange", amount.Message);
        var currency = await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(null, 200m, null, null, "xyz", null, null)));
        Assert.Equal("orders.errors.invalidCurrency", currency.Message);
        Assert.Equal((100m, (decimal?)null, "USD"), (b.QuotedAmount, b.DepositAmount, b.CurrencyCode));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Completed, OrderStatus.Canceled)]
    [InlineData(OrderStatus.Completed, OrderStatus.Considering)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Quoted)]
    [InlineData(OrderStatus.DepositPaid, OrderStatus.Considering)]
    [InlineData(OrderStatus.Lost, OrderStatus.Completed)]
    [InlineData(OrderStatus.Canceled, OrderStatus.Confirmed)]
    public async Task Update_refuses_a_status_move_the_table_does_not_allow(string from, string to)
    {
        var b = MakeOrder(from);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(to, null, null, null, null, null, null)));
        Assert.Equal("orders.errors.statusChange", ex.Message);
        Assert.Equal(from, b.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Considering, OrderStatus.Completed)]   // steps may be skipped
    [InlineData(OrderStatus.Quoted, OrderStatus.Considering)]
    [InlineData(OrderStatus.DepositPaid, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.DepositPaid)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Canceled)]
    [InlineData(OrderStatus.Lost, OrderStatus.Considering)]        // reopened
    [InlineData(OrderStatus.Canceled, OrderStatus.Quoted)]
    [InlineData(OrderStatus.Completed, OrderStatus.Completed)]     // the same status again (the popup sends it with every save)
    public async Task Update_allows_the_moves_in_the_table(string from, string to)
    {
        var b = MakeOrder(from);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(to, null, 50m, null, null, null, null));
        Assert.Equal((to, (decimal?)50m), (b.Status, b.DepositAmount));
    }

    [Fact]
    public void Every_status_has_a_row_in_the_table_and_completed_is_final()
    {
        foreach (var status in OrderStatus.All)
        {
            Assert.All(OrderService.NextStatuses(status), next => Assert.Contains(next, OrderStatus.All));
            Assert.DoesNotContain(status, OrderService.NextStatuses(status));
        }
        Assert.Empty(OrderService.NextStatuses(OrderStatus.Completed));
        Assert.Equal(6, OrderService.NextStatuses(OrderStatus.Considering).Count);
    }

    [Theory]
    [InlineData(OrderStatus.Canceled)]
    [InlineData(OrderStatus.Lost)]
    public async Task Canceling_the_only_confirmed_order_moves_the_customer_to_the_closed_stage(string status)
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Customer };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(OrderStatus.Confirmed, contact.Id);
        // The read-only list still shows this order as confirmed (it is the one being changed) plus an old lost one.
        _orders.Setup(r => r.ListForContactReadOnlyAsync(_clientId, contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new Order { Id = b.Id, Status = OrderStatus.Confirmed }, new Order { Id = Guid.NewGuid(), Status = OrderStatus.Lost }]);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(status, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.NotInterested, contact.StageKey);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Canceling_keeps_the_customer_when_another_confirmed_order_remains()
    {
        var contact = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Customer };
        _contacts.Setup(l => l.GetByIdAsync(contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var b = MakeOrder(OrderStatus.Confirmed, contact.Id);
        _orders.Setup(r => r.ListForContactReadOnlyAsync(_clientId, contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new Order { Id = b.Id, Status = OrderStatus.Confirmed }, new Order { Id = Guid.NewGuid(), Status = OrderStatus.Completed }]);
        await Sut().UpdateAsync(_clientId, b.Id, new UpdateOrderRequest(OrderStatus.Canceled, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.Customer, contact.StageKey);
    }

    [Fact]
    public async Task Canceling_leaves_a_contact_someone_already_moved_on_and_ignores_an_unconfirmed_order()
    {
        var moved = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.NeedsHuman };
        _contacts.Setup(l => l.GetByIdAsync(moved.Id, It.IsAny<CancellationToken>())).ReturnsAsync(moved);
        await Sut().UpdateAsync(_clientId, MakeOrder(OrderStatus.DepositPaid, moved.Id).Id,
            new UpdateOrderRequest(OrderStatus.Lost, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.NeedsHuman, moved.StageKey);

        // A quote that is canceled never made them a customer: the stage isn't touched.
        var customer = new Contact { Id = Guid.NewGuid(), StageKey = PipelineKit.Customer };
        _contacts.Setup(l => l.GetByIdAsync(customer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        await Sut().UpdateAsync(_clientId, MakeOrder(OrderStatus.Quoted, customer.Id).Id, new UpdateOrderRequest(OrderStatus.Canceled, null, null, null, null, null, null));
        Assert.Equal(PipelineKit.Customer, customer.StageKey);
        _orders.Verify(r => r.ListForContactReadOnlyAsync(_clientId, customer.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_maps_rows()
    {
        var b = MakeOrder();
        b.Service = new Service { Name = "Rhino" };
        _orders.Setup(r => r.ListAsync(_clientId, "quoted", 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Order> { b } as IReadOnlyList<Order>, 3));
        var (items, total) = await Sut().ListAsync(_clientId, "quoted", 0, 10);
        Assert.Equal((1, 3), (items.Count, total));
    }
}

public class NotificationServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();

    private NotificationService Sut(IStringLocalizer? text = null) => new(_repo.Object, _uow.Object, _notifier.Object, text ?? TextKit.Localizer());

    [Fact]
    public async Task Create_saves_and_pushes_live()
    {
        Notification? added = null;
        _repo.Setup(r => r.Add(It.IsAny<Notification>())).Callback<Notification>(n => added = n);
        var contact = Guid.NewGuid();
        await Sut().CreateAsync(_clientId, NotificationType.NewContact,
            NotificationText.Of("notifications.newContact.title", "notifications.newContact.message", TextArg.Of("notifications.anyone.newContact")),
            contactId: contact, link: "/x");
        Assert.Equal((NotificationType.NewContact, "notifications.newContact.title", "notifications.newContact.message", contact, false, "/x"),
            (added!.Type, added.Title, added.Message, added.ContactId, added.IsRead, added.Link));
        Assert.Equal(["[[key:notifications.anyone.newContact]]"], added.TextArgs);
        _notifier.Verify(n => n.NotificationCreatedAsync(_clientId, It.Is<NotificationResponse>(r => r.Id == added.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_never_throws_even_when_saving_or_pushing_fails()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await Sut().CreateAsync(_clientId, "t", NotificationText.Of("title", null));
        _notifier.Verify(n => n.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _notifier.Setup(n => n.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationResponse>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("hub"));
        await Sut().CreateAsync(_clientId, "t", NotificationText.Of("title", null));
    }

    [Fact]
    public async Task List_returns_counts_and_unread()
    {
        _repo.Setup(r => r.ListAsync(_clientId, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Notification> { new() { Id = Guid.NewGuid(), Title = "a" } } as IReadOnlyList<Notification>, 7, 3));
        var r = await Sut().ListAsync(_clientId, 0, 10);
        Assert.Equal((1, 3, 7), (r.Items.Count, r.UnreadCount, r.TotalCount));
        _repo.Setup(x => x.CountUnreadAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        Assert.Equal(4, await Sut().GetUnreadCountAsync(_clientId));
    }

    [Fact]
    public async Task List_shows_each_notification_in_the_readers_language_and_words()
    {
        var keyed = new Notification
        {
            Id = Guid.NewGuid(), Title = "notifications.booking.created.title", Message = "notifications.booking.created.message",
            TextArgs = ["[[key:notifications.anyone.contact]]", "Facelift", "[[time:2026-10-18T10:00:00Z]]"] // leftover-ok: sample service name
        };
        var old = new Notification { Id = Guid.NewGuid(), Title = "Booking made", Message = "Ann — Facelift booked." }; // leftover-ok: sample service name
        _repo.Setup(r => r.ListAsync(_clientId, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Notification> { keyed, old } as IReadOnlyList<Notification>, 2, 2));
        var words = TermPlaceholders.Build(new Dictionary<string, TermForms>(TextKit.DefaultTerms())
        {
            [TermName.Contact] = new("patient", "patients"), [TermName.Booking] = new("consultation", "consultations") // leftover-ok: clinic words
        });

        var items = (await Sut(TextKit.Localizer(terms: words)).ListAsync(_clientId, 0, 10)).Items;

        Assert.Equal(("Consultation made", "A patient — Facelift booked [[time:2026-10-18T10:00:00Z]]."), (items[0].Title, items[0].Message)); // leftover-ok: clinic words
        Assert.Equal(("Booking made", "Ann — Facelift booked."), (items[1].Title, items[1].Message)); // written before keys: as stored
    }

    [Fact]
    public async Task MarkRead_is_not_found_for_a_missing_one_ignores_an_already_read_one_and_stamps_unread()
    {
        // QA t1-accounts-17: another client's or an unknown notification was a silent 204.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().MarkReadAsync(_clientId, Guid.NewGuid()));
        var read = new Notification { Id = Guid.NewGuid(), IsRead = true };
        _repo.Setup(r => r.GetAsync(_clientId, read.Id, It.IsAny<CancellationToken>())).ReturnsAsync(read);
        await Sut().MarkReadAsync(_clientId, read.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var unread = new Notification { Id = Guid.NewGuid(), IsRead = false };
        _repo.Setup(r => r.GetAsync(_clientId, unread.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unread);
        await Sut().MarkReadAsync(_clientId, unread.Id);
        Assert.True(unread.IsRead);
        Assert.NotNull(unread.ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_delegates()
    {
        await Sut().MarkAllReadAsync(_clientId);
        _repo.Verify(r => r.MarkAllReadAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
