using Omni.App.Business.Services.Bookings;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Tests.Services;

public class BookingServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IServiceRepository> _serviceRows = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IAvailabilityService> _availability = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICalendarIntegrationService> _calendar = new();
    private readonly Client _client;
    private readonly Contact _contact;
    private readonly List<Booking> _upcoming = new();

    public BookingServiceTests()
    {
        _client = new Client { Id = _clientId, Timezone = "UTC" };
        _contact = new Contact { Id = _contactId, ClientId = _clientId, FullName = "Ann Lee", StageKey = PipelineKit.New };
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(_client);
        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _contacts.Setup(l => l.GetByIdAsync(_contactId, It.IsAny<CancellationToken>())).ReturnsAsync(_contact);
        _bookings.Setup(a => a.ListUpcomingForContactAsync(_clientId, _contactId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _upcoming.ToList());
        _bookings.Setup(a => a.LoadDetailsAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()))
            .Callback<Booking, CancellationToken>((a, _) => a.Contact ??= _contact).Returns(Task.CompletedTask);
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, DateTimeOffset s, DateTimeOffset? e, Guid? _, CancellationToken _) => new SlotCheck(null, e ?? s.AddMinutes(30)));
    }

    private readonly Mock<ICampaignRepository> _campaignRepo = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IEntitlementService> _entitlements = new();

    private readonly Mock<IBusinessTypeService> _businessTypes = PipelineKit.Types();

    private BookingService Sut() => new(_bookings.Object, _contacts.Object, _clients.Object, _serviceRows.Object, _uow.Object,
        _events.Object, _services.Object, _availability.Object, _notifier.Object, _notifications.Object, _calendar.Object,
        _campaignRepo.Object, _config.Object, _businessTypes.Object, TextKit.Localizer(), _entitlements.Object);

    [Fact]
    public async Task A_booking_is_credited_to_the_campaign_the_contact_replied_to()
    {
        _config.SetupGet(c => c.CampaignsBookingAttributionDays).Returns(14);
        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var send = new CampaignRecipient { Id = Guid.NewGuid(), Status = CampaignRecipientStatus.Replied, RepliedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        _campaignRepo.Setup(r => r.FindLatestRepliedAwaitingBookingAsync(_clientId, _contactId, It.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow.AddDays(-13) && d > DateTimeOffset.UtcNow.AddDays(-15)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(send);

        await Sut().CreateAsync(Req());

        Assert.Equal(CampaignRecipientStatus.Booked, send.Status);
        Assert.NotNull(send.BookedAt);
    }

    [Fact]
    public async Task A_booking_without_a_recent_campaign_reply_credits_nothing()
    {
        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _config.SetupGet(c => c.CampaignsBookingAttributionDays).Returns(14);
        await Sut().CreateAsync(Req());
        _campaignRepo.Verify(r => r.FindLatestRepliedAwaitingBookingAsync(_clientId, _contactId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private Booking MakeBooking(string status = BookingStatus.Booked, int daysAhead = 3, Guid? serviceId = null)
    {
        var a = new Booking
        {
            Id = Guid.NewGuid(), ClientId = _clientId, ContactId = _contactId, ServiceId = serviceId, Status = status, BookingType = "standard",
            ScheduledStart = DateTimeOffset.UtcNow.AddDays(daysAhead).Date.AddHours(10), Contact = _contact
        };
        a.ScheduledStart = new DateTimeOffset(a.ScheduledStart.DateTime, TimeSpan.Zero);
        a.ScheduledEnd = a.ScheduledStart.AddMinutes(30);
        return a;
    }

    private void Setup(Booking a)
    {
        _upcoming.Add(a);
        _bookings.Setup(r => r.GetForContactAsync(_clientId, _contactId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _bookings.Setup(r => r.GetAsync(_clientId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _bookings.Setup(r => r.GetWithServiceReadOnlyAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _bookings.Setup(r => r.GetWithDetailsAsync(_clientId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
    }

    private CreateBookingRequest Req(DateTimeOffset? start = null, Guid? serviceId = null) =>
        new(_clientId, _contactId, serviceId, "", start ?? DateTimeOffset.UtcNow.AddDays(2), null, null, null, "note");

    // ---------------------------------------------------------------- booking

    [Fact]
    public async Task BookAvailableSlot_creates_updates_contact_commits_then_notifies()
    {
        Booking? saved = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(a => saved = a);
        var procId = Guid.NewGuid();

        var r = await Sut().BookAvailableSlotAsync(Req(serviceId: procId));

        Assert.Equal(saved!.Id, r.Id);
        Assert.Equal(("standard", BookingStatus.Booked), (saved.BookingType, saved.Status));
        Assert.Equal(saved.ScheduledStart.AddMinutes(30), saved.ScheduledEnd);
        Assert.Equal(PipelineKit.Booked, _contact.StageKey);
        _bookings.Verify(a => a.LockClientForBookingAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
        _services.Verify(p => p.EnsureUsableAsync(_clientId, procId, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.BookingCreated, _contactId, null, saved.Id, null, "{}"), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, saved.Id, "created", It.IsAny<CancellationToken>()), Times.Once);
        _calendar.Verify(c => c.TriggerBookingSyncAsync(_clientId, It.IsAny<BookingResponse>(), "created", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.BookingCreated,
            It.Is<NotificationText>(t => TextKit.English(t).Title == "Booking made" && TextKit.English(t).Message!.StartsWith("Ann Lee — a slot booked [[time:")), _contactId, null, saved.Id, null, It.Is<string?>(l => l!.EndsWith(saved.Id.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BookAvailableSlot_rejects_unknown_contact_existing_booking_and_unavailable_slot()
    {
        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().BookAvailableSlotAsync(Req()));

        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var existing = MakeBooking();
        _upcoming.Add(existing);
        var ex = await Assert.ThrowsAsync<ContactAlreadyBookedException>(() => Sut().BookAvailableSlotAsync(Req()));
        Assert.Equal(existing.Id, ex.Existing.Id);

        _upcoming.Clear();
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("taken", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<SlotUnavailableException>(() => Sut().BookAvailableSlotAsync(Req()));
        _bookings.Verify(a => a.Add(It.IsAny<Booking>()), Times.Never);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_member_booking_is_checked_under_the_client_lock_fills_its_end_and_a_failing_in_app_notifier_is_ignored()
    {
        // QA t5-ops-01: the dashboard booking skipped every rule.
        _notifier.Setup(n => n.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Booking? saved = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(a => saved = a);
        var r = await Sut().CreateAsync(Req());
        Assert.Equal(BookingStatus.Booked, r.Status);
        Assert.Equal(saved!.ScheduledStart.AddMinutes(30), saved.ScheduledEnd);   // the service/default length when none was given
        _bookings.Verify(a => a.LockClientForBookingAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
        _availability.Verify(a => a.OverlapsBookingAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.BookingCreated, It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_member_can_never_book_on_top_of_another_booking()
    {
        _availability.Setup(a => a.OverlapsBookingAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ex = await Assert.ThrowsAsync<OverlappingRecordException>(() => Sut().CreateAsync(Req() with { OverrideRules = true }));
        Assert.Equal("bookings.errors.overlap", ex.Message);
        _bookings.Verify(a => a.Add(It.IsAny<Booking>()), Times.Never);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SlotCheckReason.Closed, "bookings.rules.closed")]
    [InlineData(SlotCheckReason.Past, "bookings.rules.past")]
    [InlineData(SlotCheckReason.Notice, "bookings.rules.notice")]
    [InlineData(SlotCheckReason.Horizon, "bookings.rules.horizon")]
    [InlineData(SlotCheckReason.Overlap, "bookings.rules.buffer")]
    [InlineData(SlotCheckReason.NotConfigured, "bookings.rules.notConfigured")]
    public async Task A_soft_rule_asks_the_member_first_and_is_booked_once_they_confirm(string reason, string key)
    {
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, DateTimeOffset s, DateTimeOffset? e, Guid? _, CancellationToken _) => new SlotCheck("no", e ?? s.AddMinutes(45), reason));

        var ex = await Assert.ThrowsAsync<BookingRulesException>(() => Sut().CreateAsync(Req()));
        Assert.Equal((key, reason, BookingRulesException.ErrorCode), (ex.Message, ex.Reason, ex.Code));
        _bookings.Verify(a => a.Add(It.IsAny<Booking>()), Times.Never);

        Booking? saved = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(a => saved = a);
        await Sut().CreateAsync(Req() with { OverrideRules = true });
        Assert.Equal(saved!.ScheduledStart.AddMinutes(45), saved.ScheduledEnd);
    }

    [Theory]
    [InlineData(0, -30, "bookings.errors.endBeforeStart")]
    [InlineData(0, 0, "bookings.errors.endBeforeStart")]
    [InlineData(0, 25 * 60, "bookings.errors.tooLong")]
    [InlineData(-400, 30, "bookings.errors.dateOutOfRange")]
    [InlineData(800, 30, "bookings.errors.dateOutOfRange")]
    public async Task Impossible_times_are_refused_before_anything_else(int startDays, int lengthMinutes, string key)
    {
        var start = DateTimeOffset.UtcNow.AddDays(startDays);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Req(start) with { ScheduledEnd = start.AddMinutes(lengthMinutes), OverrideRules = true }));
        Assert.Equal(key, ex.Message);
        _bookings.Verify(a => a.LockClientForBookingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // QA t5-ops-02: over-long booking type, place or place name was a 500 (value too long for the column).
    [Theory]
    [InlineData(51, null, 0, "bookings.errors.typeTooLong")]
    [InlineData(0, "rooftop", 0, "bookings.errors.unknownLocation")]
    [InlineData(0, "in_person_and_also_much_longer_than_thirty", 0, "bookings.errors.unknownLocation")]
    [InlineData(0, null, 201, "bookings.errors.locationNameTooLong")]
    public async Task A_members_booking_texts_must_fit_and_its_place_must_be_known(int typeLength, string? location, int nameLength, string key)
    {
        var request = Req() with { BookingType = new string('t', typeLength), LocationType = location, LocationName = nameLength == 0 ? null : new string('n', nameLength) };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(request));
        Assert.Equal(key, ex.Message);
        _bookings.Verify(a => a.Add(It.IsAny<Booking>()), Times.Never);
        _bookings.Verify(a => a.LockClientForBookingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Booking_texts_are_trimmed_and_fit_exactly_at_their_limits()
    {
        Booking? added = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(b => added = b);
        await Sut().CreateAsync(Req() with { BookingType = " " + new string('t', 50) + " ", LocationType = " video ", LocationName = new string('n', 200) });
        Assert.Equal((new string('t', 50), "video", 200), (added!.BookingType, added.LocationType, added.LocationName!.Length));

        await Sut().CreateAsync(Req(DateTimeOffset.UtcNow.AddDays(3)) with { BookingType = "  ", LocationType = " ", LocationName = " " });
        Assert.Equal(("standard", (string?)null, (string?)null), (added!.BookingType, added.LocationType, added.LocationName));
    }

    [Fact]
    public async Task The_AI_path_keeps_short_unknown_places_but_refuses_over_long_texts()
    {
        Booking? added = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(b => added = b);
        await Sut().BookAvailableSlotAsync(Req() with { LocationType = "studio" });
        Assert.Equal("studio", added!.LocationType);

        added = null;
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().BookAvailableSlotAsync(Req() with { LocationType = new string('x', 31) }));
        Assert.Equal("bookings.errors.unknownLocation", ex.Message);
        ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().BookAvailableSlotAsync(Req() with { BookingType = new string('x', 51) }));
        Assert.Equal("bookings.errors.typeTooLong", ex.Message);
        Assert.Null(added);
    }

    [Fact]
    public async Task Create_rejects_a_contact_from_another_client()
    {
        _contacts.Setup(l => l.ExistsAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _contacts.Setup(l => l.GetAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Req()));
        _bookings.Verify(a => a.Add(It.IsAny<Booking>()), Times.Never);
    }

    // ---------------------------------------------------------------- status

    [Fact]
    public async Task UpdateStatus_validates_and_returns_null_when_missing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStatusAsync(_clientId, Guid.NewGuid(), "bogus"));
        Assert.Null(await Sut().UpdateStatusAsync(_clientId, Guid.NewGuid(), BookingStatus.Attended));
    }

    [Theory]
    [InlineData(BookingStatus.Attended, PipelineKit.Attended, EventTypes.BookingAttended, "status_changed")]
    [InlineData(BookingStatus.NoShow, PipelineKit.NoShow, EventTypes.BookingNoShow, "status_changed")]
    [InlineData(BookingStatus.Confirmed, PipelineKit.New, null, "status_changed")]
    [InlineData(BookingStatus.Canceled, PipelineKit.New, null, "canceled")]
    public async Task UpdateStatus_moves_the_contact_and_logs(string status, string expectedContact, string? eventType, string change)
    {
        var a = MakeBooking();
        Setup(a);
        var r = await Sut().UpdateStatusAsync(_clientId, a.Id, status);
        Assert.Equal(status, r!.Status);
        Assert.Equal(expectedContact, _contact.StageKey);
        _events.Verify(e => e.Log(_clientId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), eventType is null ? Times.Never : Times.Once);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, a.Id, change, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_changes_other_than_book_move_cancel_send_no_in_app_notification()
    {
        var a = MakeBooking();
        Setup(a);
        await Sut().UpdateStatusAsync(_clientId, a.Id, BookingStatus.Attended);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- a member's edit (QA t5-ops-04)

    [Fact]
    public async Task A_member_moves_a_booking_in_utc_keeps_its_status_logs_notifies_and_updates_calendars()
    {
        var a = MakeBooking(BookingStatus.Confirmed);
        Setup(a);
        var from = a.ScheduledStart;
        var start = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20).AddHours(11), DateTimeKind.Unspecified), TimeSpan.FromHours(2));   // 09:00 UTC
        var newService = Guid.NewGuid();

        var r = await Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(start, start.AddMinutes(45), newService, " video ", "  bring the form  "));

        Assert.Equal((TimeSpan.Zero, start, start.AddMinutes(45), newService, "video", "bring the form", BookingStatus.Confirmed),
            (a.ScheduledStart.Offset, a.ScheduledStart, a.ScheduledEnd, a.ServiceId, a.LocationType, a.Notes, a.Status));
        Assert.Equal(a.Id, r!.Id);
        _services.Verify(p => p.EnsureUsableAsync(_clientId, newService, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.BookingRescheduled, _contactId, null, a.Id, null,
            It.Is<string>(m => m.Contains("\"by\":\"member\"") && m.Contains(from.ToString("yyyy-MM-dd")))), Times.Once);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, a.Id, "rescheduled", It.IsAny<CancellationToken>()), Times.Once);
        _calendar.Verify(c => c.TriggerBookingSyncAsync(_clientId, It.IsAny<BookingResponse>(), "rescheduled", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.BookingRescheduled, It.IsAny<NotificationText>(), _contactId, null, a.Id, null,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        // The move is checked like a new booking, with this booking left out (QA t5-ops-01).
        _availability.Verify(x => x.OverlapsBookingAsync(_clientId, start, start.AddMinutes(45), a.Id, It.IsAny<CancellationToken>()), Times.Once);
        _bookings.Verify(x => x.LockClientForBookingAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_member_cant_move_a_booking_on_top_of_another_and_is_asked_before_moving_it_outside_opening_hours()
    {
        var a = MakeBooking();
        Setup(a);
        var before = a.ScheduledStart;
        var start = a.ScheduledStart.AddHours(2);
        _availability.Setup(x => x.OverlapsBookingAsync(_clientId, start, start.AddMinutes(30), a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<OverlappingRecordException>(() => Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(start, start.AddMinutes(30), null, null, null, true)));

        var later = a.ScheduledStart.AddHours(5);
        _availability.Setup(x => x.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), later, It.IsAny<DateTimeOffset?>(), a.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("closed", later.AddMinutes(30), SlotCheckReason.Closed));
        await Assert.ThrowsAsync<BookingRulesException>(() => Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(later, later.AddMinutes(30), null, null, null)));
        Assert.Equal(before, a.ScheduledStart);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(later, later.AddMinutes(30), null, null, null, true));
        Assert.Equal(later, a.ScheduledStart);
    }

    [Fact]
    public async Task Editing_only_the_details_is_an_update_without_a_reschedule_or_notification()
    {
        var oldService = Guid.NewGuid();
        var a = MakeBooking(serviceId: oldService);
        a.LocationType = "kiosk";   // a place another tool set may stay
        Setup(a);

        await Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(a.ScheduledStart, a.ScheduledEnd, oldService, "kiosk", "new note"));

        Assert.Equal(("new note", "kiosk"), (a.Notes, a.LocationType));
        _services.Verify(p => p.EnsureUsableAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);   // kept, not re-checked
        _events.Verify(e => e.Log(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, a.Id, "updated", It.IsAny<CancellationToken>()), Times.Once);
        _calendar.Verify(c => c.TriggerBookingSyncAsync(_clientId, It.IsAny<BookingResponse>(), "updated", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Clearing the service, place, notes and length.
        await Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(a.ScheduledStart, null, null, "", " "));
        // A booking always keeps an end: without one it takes the service or default length (QA t5-ops-01).
        Assert.Equal(((Guid?)null, (string?)null, (string?)null, a.ScheduledStart.AddMinutes(30)), (a.ServiceId, a.LocationType, a.Notes, a.ScheduledEnd));
    }

    [Theory]
    [InlineData(BookingStatus.Attended, 0, null, "bookings.errors.cantEdit")]
    [InlineData(BookingStatus.Canceled, 0, null, "bookings.errors.cantEdit")]
    [InlineData(BookingStatus.Booked, -30, null, "bookings.errors.endBeforeStart")]
    [InlineData(BookingStatus.Booked, 0, "garden", "bookings.errors.unknownLocation")]
    public async Task A_booking_that_cant_change_or_a_bad_edit_is_refused_and_nothing_changes(string status, int endMinutes, string? location, string key)
    {
        var a = MakeBooking(status);
        Setup(a);
        var before = (a.ScheduledStart, a.ScheduledEnd, a.LocationType);
        var start = a.ScheduledStart.AddHours(1);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(start, endMinutes == 0 ? null : start.AddMinutes(endMinutes), null, location, null)));
        Assert.Equal(key, ex.Message);
        Assert.Equal(before, (a.ScheduledStart, a.ScheduledEnd, a.LocationType));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Editing_another_clients_booking_is_not_found_and_a_new_service_must_be_usable()
    {
        Assert.Null(await Sut().UpdateAsync(_clientId, Guid.NewGuid(), new UpdateBookingRequest(DateTimeOffset.UtcNow, null, null, null, null)));

        var a = MakeBooking();
        Setup(a);
        _services.Setup(p => p.EnsureUsableAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("services.errors.inactive"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, a.Id, new UpdateBookingRequest(a.ScheduledStart, a.ScheduledEnd, Guid.NewGuid(), null, null)));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- reschedule / cancel

    [Fact]
    public async Task Reschedule_moves_the_single_upcoming_booking_and_resets_status()
    {
        var a = MakeBooking(BookingStatus.Confirmed);
        Setup(a);
        var newStart = DateTimeOffset.UtcNow.AddDays(5);
        var r = await Sut().RescheduleAsync(_clientId, _contactId, null, newStart, null, "contact asked");
        Assert.Equal(BookingStatus.Booked, r!.Status);
        Assert.Equal(newStart.ToUniversalTime(), a.ScheduledStart);
        Assert.Equal(newStart.AddMinutes(30).ToUniversalTime(), a.ScheduledEnd);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, null, newStart, null, a.Id, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.BookingRescheduled, _contactId, null, a.Id, null, "{\"reason\":\"contact asked\"}"), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, a.Id, "rescheduled", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_uses_the_service_only_when_it_is_still_active()
    {
        var procId = Guid.NewGuid();
        var a = MakeBooking(serviceId: procId);
        Setup(a);
        _serviceRows.Setup(p => p.IsActiveAsync(procId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().RescheduleAsync(_clientId, _contactId, a.Id, DateTimeOffset.UtcNow.AddDays(5), null, null);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, procId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), a.Id, It.IsAny<CancellationToken>()), Times.Once);

        _serviceRows.Setup(p => p.IsActiveAsync(procId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Sut().RescheduleAsync(_clientId, _contactId, a.Id, DateTimeOffset.UtcNow.AddDays(6), null, null);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), a.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_guards()
    {
        Assert.Null(await Sut().RescheduleAsync(_clientId, _contactId, null, DateTimeOffset.UtcNow.AddDays(5), null, null)); // nothing upcoming
        Assert.Null(await Sut().RescheduleAsync(_clientId, _contactId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(5), null, null)); // unknown id

        var done = MakeBooking(BookingStatus.Attended);
        Setup(done);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescheduleAsync(_clientId, _contactId, done.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));

        var past = MakeBooking(daysAhead: -2);
        Setup(past);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescheduleAsync(_clientId, _contactId, past.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));
        Assert.Contains("in the past", TextKit.English(ex));

        var ok = MakeBooking();
        Setup(ok);
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("busy", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<SlotUnavailableException>(() => Sut().RescheduleAsync(_clientId, _contactId, ok.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));
    }

    [Fact]
    public async Task Reschedule_without_id_and_two_upcoming_asks_which_one()
    {
        Setup(MakeBooking());
        Setup(MakeBooking(daysAhead: 4));
        var ex = await Assert.ThrowsAsync<MultipleUpcomingBookingsException>(() => Sut().RescheduleAsync(_clientId, _contactId, null, DateTimeOffset.UtcNow.AddDays(9), null, null));
        Assert.Equal(2, ex.Bookings.Count);
    }

    [Fact]
    public async Task Cancel_marks_canceled_logs_and_notifies_and_is_idempotent()
    {
        var a = MakeBooking();
        Setup(a);
        var r = await Sut().CancelAsync(_clientId, _contactId, null, "changed mind");
        Assert.Equal(BookingStatus.Canceled, r!.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.BookingCanceled, _contactId, null, a.Id, null, "{\"reason\":\"changed mind\"}"), Times.Once);
        _notifier.Verify(n => n.BookingChangedAsync(_clientId, a.Id, "canceled", It.IsAny<CancellationToken>()), Times.Once);

        // second call: already canceled â†’ returned as-is, no new event or notification
        var again = await Sut().CancelAsync(_clientId, _contactId, a.Id, null);
        Assert.Equal(BookingStatus.Canceled, again!.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.BookingCanceled, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_rejects_finished_bookings_and_returns_null_when_nothing_matches()
    {
        Assert.Null(await Sut().CancelAsync(_clientId, _contactId, null, null));
        Assert.Null(await Sut().CancelAsync(_clientId, _contactId, Guid.NewGuid(), null));
        var attended = MakeBooking(BookingStatus.Attended);
        Setup(attended);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CancelAsync(_clientId, _contactId, attended.Id, null));
    }

    // ---------------------------------------------------------------- booking context / schedule (AI tool)

    [Fact]
    public async Task BookingContext_reports_blocked_when_an_upcoming_booking_exists()
    {
        Assert.Null(await Sut().GetBookingContextAsync(_clientId, Guid.NewGuid()));
        var free = await Sut().GetBookingContextAsync(_clientId, _contactId);
        Assert.False(free!.HasUpcomingBooking);
        Assert.True(free.CanCreateNewBooking);
        Assert.Null(free.BookingBlockReason);

        Setup(MakeBooking());
        var blocked = await Sut().GetBookingContextAsync(_clientId, _contactId);
        Assert.True(blocked!.HasUpcomingBooking);
        Assert.False(blocked.CanCreateNewBooking);
        Assert.Equal("existing_upcoming_booking", blocked.BookingBlockReason);
    }

    [Fact]
    public async Task Schedule_books_a_new_booking_when_none_exists()
    {
        Booking? saved = null;
        _bookings.Setup(a => a.Add(It.IsAny<Booking>())).Callback<Booking>(a => { saved = a; _upcoming.Add(a); });
        var o = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(3), Notes: "n"));
        Assert.Equal(("created", "BOOKED"), (o.Operation, o.Code));
        Assert.Equal(saved!.Id, o.Booking!.Id);
        Assert.NotNull(o.RequestedLabel);
    }

    [Fact]
    public async Task Schedule_reports_contact_not_found()
    {
        var o = await Sut().ScheduleAsync(_clientId, Guid.NewGuid(), new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(3)));
        Assert.Equal("CONTACT_NOT_FOUND", o.Code);
    }

    [Fact]
    public async Task Schedule_with_existing_booking_needs_confirmation_then_reschedules()
    {
        var a = MakeBooking();
        Setup(a);
        var when = DateTimeOffset.UtcNow.AddDays(6);

        var ask = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(when));
        Assert.Equal("CONFIRMATION_REQUIRED", ask.Code);
        Assert.Equal(a.Id, Assert.Single(ask.Existing!).Id);

        var moved = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(when, ConfirmReplaceExisting: true, Reason: "r"));
        Assert.Equal(("rescheduled", "RESCHEDULED"), (moved.Operation, moved.Code));
        Assert.Equal(a.Id, moved.PreviousBooking!.Id);
        Assert.Equal(when.ToUniversalTime(), a.ScheduledStart);
    }

    [Fact]
    public async Task Schedule_with_two_upcoming_requires_a_valid_booking_id()
    {
        var a1 = MakeBooking();
        var a2 = MakeBooking(daysAhead: 4);
        Setup(a1);
        Setup(a2);
        var when = DateTimeOffset.UtcNow.AddDays(8);

        var bad = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(when, ConfirmReplaceExisting: true, BookingId: Guid.NewGuid()));
        Assert.Equal("INVALID_REQUEST", bad.Code);

        var unclear = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(when, ConfirmReplaceExisting: true));
        Assert.Equal("MULTIPLE_UPCOMING_BOOKINGS", unclear.Code);

        var ok = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(when, ConfirmReplaceExisting: true, BookingId: a2.Id));
        Assert.Equal("RESCHEDULED", ok.Code);
    }

    [Fact]
    public async Task Schedule_maps_slot_problems_to_codes()
    {
        var a = MakeBooking();
        Setup(a);
        _availability.Setup(x => x.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("Closed that day", DateTimeOffset.UtcNow));
        var o = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(6), ConfirmReplaceExisting: true));
        Assert.Equal(("SLOT_UNAVAILABLE", "Closed that day"), (o.Code, o.Message));

        _upcoming.Clear();
        var fresh = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(6)));
        Assert.Equal("SLOT_UNAVAILABLE", fresh.Code);
    }

    [Fact]
    public async Task Schedule_turns_a_race_with_another_booking_into_a_confirmation_request()
    {
        // The context said "free", but by the time the transaction locked the client someone had booked.
        var raced = MakeBooking();
        var calls = 0;
        _bookings.Setup(a => a.ListUpcomingForContactAsync(_clientId, _contactId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? new List<Booking>() : new List<Booking> { raced });
        var o = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(3)));
        Assert.Equal("CONFIRMATION_REQUIRED", o.Code);
        Assert.Equal(raced.Id, Assert.Single(o.Existing!).Id);
    }

    [Fact]
    public async Task Schedule_turns_argument_errors_into_invalid_request()
    {
        _services.Setup(p => p.EnsureUsableAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Service is inactive"));
        var o = await Sut().ScheduleAsync(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(3), ServiceId: Guid.NewGuid()));
        Assert.Equal(("INVALID_REQUEST", "Service is inactive"), (o.Code, o.Message));
    }

    [Fact]
    public async Task CancelBooking_outcomes()
    {
        var none = await Sut().CancelBookingAsync(_clientId, _contactId, null, null);
        Assert.Equal("NO_UPCOMING_BOOKING", none.Code);

        var a1 = MakeBooking();
        Setup(a1);
        var one = await Sut().CancelBookingAsync(_clientId, _contactId, null, "r");
        Assert.Equal(("canceled", "CANCELED"), (one.Operation, one.Code));
        Assert.Equal(a1.Id, one.Booking!.Id);

        var a2 = MakeBooking(daysAhead: 4);
        var a3 = MakeBooking(daysAhead: 5);
        _upcoming.Clear();
        Setup(a2);
        Setup(a3);
        var multiple = await Sut().CancelBookingAsync(_clientId, _contactId, null, null);
        Assert.Equal("MULTIPLE_UPCOMING_BOOKINGS", multiple.Code);
        Assert.Equal(2, multiple.Existing!.Count);

        var invalid = await Sut().CancelBookingAsync(_clientId, _contactId, Guid.NewGuid(), null);
        Assert.Equal("INVALID_REQUEST", invalid.Code);

        var specific = await Sut().CancelBookingAsync(_clientId, _contactId, a3.Id, null);
        Assert.Equal("CANCELED", specific.Code);
        Assert.Equal(BookingStatus.Canceled, a3.Status);
        Assert.Equal(BookingStatus.Booked, a2.Status);
    }

    [Fact]
    public async Task CancelBooking_ignores_the_id_when_exactly_one_is_upcoming()
    {
        // By design (see the comment in CancelBookingAsync): the AI's booking ids go stale, so with one upcoming
        // booking the backend selects it itself. This was reported as BUG-03 and withdrawn.
        var a = MakeBooking();
        Setup(a);
        var o = await Sut().CancelBookingAsync(_clientId, _contactId, Guid.NewGuid(), null);
        Assert.Equal("CANCELED", o.Code);
        Assert.Equal(BookingStatus.Canceled, a.Status);
    }

    // ---------------------------------------------------------------- queries

    [Fact]
    public async Task GetUpcomingForContact_converts_times_to_client_zone()
    {
        _client.Timezone = "Asia/Beirut";
        var a = MakeBooking();
        Setup(a);
        var r = await Sut().GetUpcomingForContactAsync(_clientId, _contactId);
        Assert.Equal("Asia/Beirut", r.Timezone);
        var item = Assert.Single(r.Bookings);
        var local = TimeZoneInfo.ConvertTime(a.ScheduledStart, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"));
        Assert.Equal((local.ToString("yyyy-MM-dd"), local.ToString("HH:mm")), (item.Date, item.Time));
        Assert.Contains(" at ", item.Label);
    }

    [Fact]
    public async Task Unknown_client_or_bad_timezone_falls_back_to_utc()
    {
        _client.Timezone = "Nope/Zone";
        Assert.Equal("UTC", (await Sut().GetUpcomingForContactAsync(_clientId, _contactId)).Timezone);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync((Client?)null);
        Assert.Equal("UTC", (await Sut().GetUpcomingForContactAsync(_clientId, _contactId)).Timezone);
    }

    [Fact]
    public async Task GetById_and_List_map_through_the_mapper()
    {
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
        var a = MakeBooking();
        Setup(a);
        Assert.Equal(a.Id, (await Sut().GetByIdAsync(_clientId, a.Id))!.Id);
        _bookings.Setup(r => r.ListAsync(_clientId, "booked", null, null, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Booking> { a } as IReadOnlyList<Booking>, 7));
        var (items, total) = await Sut().ListAsync(_clientId, "booked", null, null, 0, 10);
        Assert.Equal((1, 7), (items.Count, total));
    }

    [Fact]
    public async Task CalendarMonth_builds_a_sunday_aligned_grid()
    {
        var a = MakeBooking();
        _bookings.Setup(r => r.ListStartingBetweenAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking> { a });
        _bookings.Setup(r => r.CountNeedingOutcomeAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var m = await Sut().GetCalendarMonthAsync(_clientId, 2026, 10);

        Assert.Equal("2026-09-27", m.GridStart); // 1 Oct 2026 is a Thursday
        Assert.Equal("2026-10-31", m.GridEnd);   // 31 Oct 2026 is a Saturday
        Assert.Equal(4, m.NeedsOutcomeCount);
        Assert.Equal("Ann Lee", Assert.Single(m.Items).ContactFullName);
        _bookings.Verify(r => r.ListStartingBetweenAsync(_clientId,
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CalendarMonth_display_zone_shifts_bounds_and_times()
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        _client.Timezone = "UTC";
        _bookings.Setup(r => r.ListStartingBetweenAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Booking>());
        var m = await Sut().GetCalendarMonthAsync(_clientId, 2026, 1, beirut);
        Assert.Equal(("Asia/Beirut", "UTC"), (m.Timezone, m.ClientTimezone));
        _bookings.Verify(r => r.ListStartingBetweenAsync(_clientId, new DateTimeOffset(2025, 12, 28, 0, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

