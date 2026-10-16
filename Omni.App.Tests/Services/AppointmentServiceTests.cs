using Omni.App.Business.Services.Appointments;
using Omni.App.Entities.Dtos.Appointments;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Appointments;
using Omni.App.Entities.Responses.Appointments;

namespace Omni.App.Tests.Services;

public class AppointmentServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _leadId = Guid.NewGuid();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ILeadRepository> _leads = new();
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
    private readonly Lead _lead;
    private readonly List<Appointment> _upcoming = new();

    public AppointmentServiceTests()
    {
        _client = new Client { Id = _clientId, Timezone = "UTC" };
        _lead = new Lead { Id = _leadId, ClientId = _clientId, FullName = "Ann Lee", Status = LeadStatus.New };
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(_client);
        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _leads.Setup(l => l.GetAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(_lead);
        _leads.Setup(l => l.GetByIdAsync(_leadId, It.IsAny<CancellationToken>())).ReturnsAsync(_lead);
        _appointments.Setup(a => a.ListUpcomingForLeadAsync(_clientId, _leadId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _upcoming.ToList());
        _appointments.Setup(a => a.LoadDetailsAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
            .Callback<Appointment, CancellationToken>((a, _) => a.Lead ??= _lead).Returns(Task.CompletedTask);
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid? _, DateTimeOffset s, DateTimeOffset? e, Guid? _, CancellationToken _) => new SlotCheck(null, e ?? s.AddMinutes(30)));
    }

    private readonly Mock<ICampaignRepository> _campaignRepo = new();
    private readonly Mock<IConfigManager> _config = new();

    private AppointmentService Sut() => new(_appointments.Object, _leads.Object, _clients.Object, _serviceRows.Object, _uow.Object,
        _events.Object, _services.Object, _availability.Object, _notifier.Object, _notifications.Object, _calendar.Object,
        _campaignRepo.Object, _config.Object);

    [Fact]
    public async Task A_booking_is_credited_to_the_campaign_the_patient_replied_to()
    {
        _config.SetupGet(c => c.CampaignsBookingAttributionDays).Returns(14);
        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var send = new CampaignRecipient { Id = Guid.NewGuid(), Status = CampaignRecipientStatus.Replied, RepliedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        _campaignRepo.Setup(r => r.FindLatestRepliedAwaitingBookingAsync(_clientId, _leadId, It.Is<DateTimeOffset>(d => d < DateTimeOffset.UtcNow.AddDays(-13) && d > DateTimeOffset.UtcNow.AddDays(-15)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(send);

        await Sut().CreateAsync(Req());

        Assert.Equal(CampaignRecipientStatus.Booked, send.Status);
        Assert.NotNull(send.BookedAt);
    }

    [Fact]
    public async Task A_booking_without_a_recent_campaign_reply_credits_nothing()
    {
        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _config.SetupGet(c => c.CampaignsBookingAttributionDays).Returns(14);
        await Sut().CreateAsync(Req());
        _campaignRepo.Verify(r => r.FindLatestRepliedAwaitingBookingAsync(_clientId, _leadId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    private Appointment MakeAppointment(string status = AppointmentStatus.Booked, int daysAhead = 3, Guid? serviceId = null)
    {
        var a = new Appointment
        {
            Id = Guid.NewGuid(), ClientId = _clientId, LeadId = _leadId, ServiceId = serviceId, Status = status, AppointmentType = "consultation",
            ScheduledStart = DateTimeOffset.UtcNow.AddDays(daysAhead).Date.AddHours(10), Lead = _lead
        };
        a.ScheduledStart = new DateTimeOffset(a.ScheduledStart.DateTime, TimeSpan.Zero);
        a.ScheduledEnd = a.ScheduledStart.AddMinutes(30);
        return a;
    }

    private void Setup(Appointment a)
    {
        _upcoming.Add(a);
        _appointments.Setup(r => r.GetForLeadAsync(_clientId, _leadId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _appointments.Setup(r => r.GetAsync(_clientId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _appointments.Setup(r => r.GetWithServiceReadOnlyAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
        _appointments.Setup(r => r.GetWithDetailsAsync(_clientId, a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(a);
    }

    private CreateAppointmentRequest Req(DateTimeOffset? start = null, Guid? serviceId = null) =>
        new(_clientId, _leadId, serviceId, "", start ?? DateTimeOffset.UtcNow.AddDays(2), null, null, null, "note");

    // ---------------------------------------------------------------- booking

    [Fact]
    public async Task BookAvailableSlot_creates_updates_lead_commits_then_notifies()
    {
        Appointment? saved = null;
        _appointments.Setup(a => a.Add(It.IsAny<Appointment>())).Callback<Appointment>(a => saved = a);
        var procId = Guid.NewGuid();

        var r = await Sut().BookAvailableSlotAsync(Req(serviceId: procId));

        Assert.Equal(saved!.Id, r.Id);
        Assert.Equal(("consultation", AppointmentStatus.Booked), (saved.AppointmentType, saved.Status));
        Assert.Equal(saved.ScheduledStart.AddMinutes(30), saved.ScheduledEnd);
        Assert.Equal(LeadStatus.ConsultationBooked, _lead.Status);
        _appointments.Verify(a => a.LockClientForBookingAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);
        _services.Verify(p => p.EnsureUsableAsync(_clientId, procId, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConsultationBooked, _leadId, null, saved.Id, null, "{}"), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.AppointmentChangedAsync(_clientId, saved.Id, "created", It.IsAny<CancellationToken>()), Times.Once);
        _calendar.Verify(c => c.TriggerAppointmentSyncAsync(_clientId, It.IsAny<AppointmentResponse>(), "created", It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.AppointmentBooked, "Appointment booked",
            It.Is<string?>(m => m!.StartsWith("Ann Lee — a consultation booked [[time:")), _leadId, null, saved.Id, null, It.Is<string?>(l => l!.EndsWith(saved.Id.ToString())), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BookAvailableSlot_rejects_unknown_lead_existing_booking_and_unavailable_slot()
    {
        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().BookAvailableSlotAsync(Req()));

        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var existing = MakeAppointment();
        _upcoming.Add(existing);
        var ex = await Assert.ThrowsAsync<LeadAlreadyBookedException>(() => Sut().BookAvailableSlotAsync(Req()));
        Assert.Equal(existing.Id, ex.Existing.Id);

        _upcoming.Clear();
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("taken", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<SlotUnavailableException>(() => Sut().BookAvailableSlotAsync(Req()));
        _appointments.Verify(a => a.Add(It.IsAny<Appointment>()), Times.Never);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_skips_availability_and_a_failing_in_app_notifier_is_ignored()
    {
        _notifier.Setup(n => n.AppointmentChangedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        var r = await Sut().CreateAsync(Req());
        Assert.Equal(AppointmentStatus.Booked, r.Status);
        _availability.Verify(a => a.CheckSlotAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.AppointmentBooked, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_rejects_a_lead_from_another_client()
    {
        _leads.Setup(l => l.ExistsAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _leads.Setup(l => l.GetAsync(_clientId, _leadId, It.IsAny<CancellationToken>())).ReturnsAsync((Lead?)null);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Req()));
        _appointments.Verify(a => a.Add(It.IsAny<Appointment>()), Times.Never);
    }

    // ---------------------------------------------------------------- status

    [Fact]
    public async Task UpdateStatus_validates_and_returns_null_when_missing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStatusAsync(_clientId, Guid.NewGuid(), "bogus"));
        Assert.Null(await Sut().UpdateStatusAsync(_clientId, Guid.NewGuid(), AppointmentStatus.Attended));
    }

    [Theory]
    [InlineData(AppointmentStatus.Attended, LeadStatus.ConsultationAttended, EventTypes.ConsultationAttended, "status_changed")]
    [InlineData(AppointmentStatus.NoShow, LeadStatus.NoShow, EventTypes.ConsultationNoShow, "status_changed")]
    [InlineData(AppointmentStatus.Confirmed, LeadStatus.New, null, "status_changed")]
    [InlineData(AppointmentStatus.Canceled, LeadStatus.New, null, "canceled")]
    public async Task UpdateStatus_moves_the_lead_and_logs(string status, string expectedLead, string? eventType, string change)
    {
        var a = MakeAppointment();
        Setup(a);
        var r = await Sut().UpdateStatusAsync(_clientId, a.Id, status);
        Assert.Equal(status, r!.Status);
        Assert.Equal(expectedLead, _lead.Status);
        _events.Verify(e => e.Log(_clientId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), eventType is null ? Times.Never : Times.Once);
        _notifier.Verify(n => n.AppointmentChangedAsync(_clientId, a.Id, change, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_changes_other_than_book_move_cancel_send_no_in_app_notification()
    {
        var a = MakeAppointment();
        Setup(a);
        await Sut().UpdateStatusAsync(_clientId, a.Id, AppointmentStatus.Attended);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- reschedule / cancel

    [Fact]
    public async Task Reschedule_moves_the_single_upcoming_appointment_and_resets_status()
    {
        var a = MakeAppointment(AppointmentStatus.Confirmed);
        Setup(a);
        var newStart = DateTimeOffset.UtcNow.AddDays(5);
        var r = await Sut().RescheduleAsync(_clientId, _leadId, null, newStart, null, "patient asked");
        Assert.Equal(AppointmentStatus.Booked, r!.Status);
        Assert.Equal(newStart.ToUniversalTime(), a.ScheduledStart);
        Assert.Equal(newStart.AddMinutes(30).ToUniversalTime(), a.ScheduledEnd);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, null, newStart, null, a.Id, It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConsultationRescheduled, _leadId, null, a.Id, null, "{\"reason\":\"patient asked\"}"), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.AppointmentChangedAsync(_clientId, a.Id, "rescheduled", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_uses_the_service_only_when_it_is_still_active()
    {
        var procId = Guid.NewGuid();
        var a = MakeAppointment(serviceId: procId);
        Setup(a);
        _serviceRows.Setup(p => p.IsActiveAsync(procId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().RescheduleAsync(_clientId, _leadId, a.Id, DateTimeOffset.UtcNow.AddDays(5), null, null);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, procId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), a.Id, It.IsAny<CancellationToken>()), Times.Once);

        _serviceRows.Setup(p => p.IsActiveAsync(procId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Sut().RescheduleAsync(_clientId, _leadId, a.Id, DateTimeOffset.UtcNow.AddDays(6), null, null);
        _availability.Verify(x => x.CheckSlotAsync(_clientId, null, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), a.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reschedule_guards()
    {
        Assert.Null(await Sut().RescheduleAsync(_clientId, _leadId, null, DateTimeOffset.UtcNow.AddDays(5), null, null)); // nothing upcoming
        Assert.Null(await Sut().RescheduleAsync(_clientId, _leadId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(5), null, null)); // unknown id

        var done = MakeAppointment(AppointmentStatus.Attended);
        Setup(done);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescheduleAsync(_clientId, _leadId, done.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));

        var past = MakeAppointment(daysAhead: -2);
        Setup(past);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().RescheduleAsync(_clientId, _leadId, past.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));
        Assert.Contains("in the past", ex.Message);

        var ok = MakeAppointment();
        Setup(ok);
        _availability.Setup(a => a.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("busy", DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<SlotUnavailableException>(() => Sut().RescheduleAsync(_clientId, _leadId, ok.Id, DateTimeOffset.UtcNow.AddDays(5), null, null));
    }

    [Fact]
    public async Task Reschedule_without_id_and_two_upcoming_asks_which_one()
    {
        Setup(MakeAppointment());
        Setup(MakeAppointment(daysAhead: 4));
        var ex = await Assert.ThrowsAsync<MultipleUpcomingAppointmentsException>(() => Sut().RescheduleAsync(_clientId, _leadId, null, DateTimeOffset.UtcNow.AddDays(9), null, null));
        Assert.Equal(2, ex.Appointments.Count);
    }

    [Fact]
    public async Task Cancel_marks_canceled_logs_and_notifies_and_is_idempotent()
    {
        var a = MakeAppointment();
        Setup(a);
        var r = await Sut().CancelAsync(_clientId, _leadId, null, "changed mind");
        Assert.Equal(AppointmentStatus.Canceled, r!.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConsultationCancelled, _leadId, null, a.Id, null, "{\"reason\":\"changed mind\"}"), Times.Once);
        _notifier.Verify(n => n.AppointmentChangedAsync(_clientId, a.Id, "canceled", It.IsAny<CancellationToken>()), Times.Once);

        // second call: already canceled â†’ returned as-is, no new event or notification
        var again = await Sut().CancelAsync(_clientId, _leadId, a.Id, null);
        Assert.Equal(AppointmentStatus.Canceled, again!.Status);
        _events.Verify(e => e.Log(_clientId, EventTypes.ConsultationCancelled, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_rejects_finished_appointments_and_returns_null_when_nothing_matches()
    {
        Assert.Null(await Sut().CancelAsync(_clientId, _leadId, null, null));
        Assert.Null(await Sut().CancelAsync(_clientId, _leadId, Guid.NewGuid(), null));
        var attended = MakeAppointment(AppointmentStatus.Attended);
        Setup(attended);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CancelAsync(_clientId, _leadId, attended.Id, null));
    }

    // ---------------------------------------------------------------- booking context / schedule (AI tool)

    [Fact]
    public async Task BookingContext_reports_blocked_when_an_upcoming_appointment_exists()
    {
        Assert.Null(await Sut().GetBookingContextAsync(_clientId, Guid.NewGuid()));
        var free = await Sut().GetBookingContextAsync(_clientId, _leadId);
        Assert.False(free!.HasUpcomingAppointment);
        Assert.True(free.CanCreateNewBooking);
        Assert.Null(free.BookingBlockReason);

        Setup(MakeAppointment());
        var blocked = await Sut().GetBookingContextAsync(_clientId, _leadId);
        Assert.True(blocked!.HasUpcomingAppointment);
        Assert.False(blocked.CanCreateNewBooking);
        Assert.Equal("existing_upcoming_appointment", blocked.BookingBlockReason);
    }

    [Fact]
    public async Task Schedule_books_a_new_appointment_when_none_exists()
    {
        Appointment? saved = null;
        _appointments.Setup(a => a.Add(It.IsAny<Appointment>())).Callback<Appointment>(a => { saved = a; _upcoming.Add(a); });
        var o = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(3), Notes: "n"));
        Assert.Equal(("created", "BOOKED"), (o.Operation, o.Code));
        Assert.Equal(saved!.Id, o.Appointment!.Id);
        Assert.NotNull(o.RequestedLabel);
    }

    [Fact]
    public async Task Schedule_reports_lead_not_found()
    {
        var o = await Sut().ScheduleAsync(_clientId, Guid.NewGuid(), new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(3)));
        Assert.Equal("LEAD_NOT_FOUND", o.Code);
    }

    [Fact]
    public async Task Schedule_with_existing_appointment_needs_confirmation_then_reschedules()
    {
        var a = MakeAppointment();
        Setup(a);
        var when = DateTimeOffset.UtcNow.AddDays(6);

        var ask = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(when));
        Assert.Equal("CONFIRMATION_REQUIRED", ask.Code);
        Assert.Equal(a.Id, Assert.Single(ask.Existing!).Id);

        var moved = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(when, ConfirmReplaceExisting: true, Reason: "r"));
        Assert.Equal(("rescheduled", "RESCHEDULED"), (moved.Operation, moved.Code));
        Assert.Equal(a.Id, moved.PreviousAppointment!.Id);
        Assert.Equal(when.ToUniversalTime(), a.ScheduledStart);
    }

    [Fact]
    public async Task Schedule_with_two_upcoming_requires_a_valid_appointment_id()
    {
        var a1 = MakeAppointment();
        var a2 = MakeAppointment(daysAhead: 4);
        Setup(a1);
        Setup(a2);
        var when = DateTimeOffset.UtcNow.AddDays(8);

        var bad = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(when, ConfirmReplaceExisting: true, AppointmentId: Guid.NewGuid()));
        Assert.Equal("INVALID_REQUEST", bad.Code);

        var unclear = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(when, ConfirmReplaceExisting: true));
        Assert.Equal("MULTIPLE_UPCOMING_APPOINTMENTS", unclear.Code);

        var ok = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(when, ConfirmReplaceExisting: true, AppointmentId: a2.Id));
        Assert.Equal("RESCHEDULED", ok.Code);
    }

    [Fact]
    public async Task Schedule_maps_slot_problems_to_codes()
    {
        var a = MakeAppointment();
        Setup(a);
        _availability.Setup(x => x.CheckSlotAsync(_clientId, It.IsAny<Guid?>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SlotCheck("Closed that day", DateTimeOffset.UtcNow));
        var o = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(6), ConfirmReplaceExisting: true));
        Assert.Equal(("SLOT_UNAVAILABLE", "Closed that day"), (o.Code, o.Message));

        _upcoming.Clear();
        var fresh = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(6)));
        Assert.Equal("SLOT_UNAVAILABLE", fresh.Code);
    }

    [Fact]
    public async Task Schedule_turns_a_race_with_another_booking_into_a_confirmation_request()
    {
        // The context said "free", but by the time the transaction locked the client someone had booked.
        var raced = MakeAppointment();
        var calls = 0;
        _appointments.Setup(a => a.ListUpcomingForLeadAsync(_clientId, _leadId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1 ? new List<Appointment>() : new List<Appointment> { raced });
        var o = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(3)));
        Assert.Equal("CONFIRMATION_REQUIRED", o.Code);
        Assert.Equal(raced.Id, Assert.Single(o.Existing!).Id);
    }

    [Fact]
    public async Task Schedule_turns_argument_errors_into_invalid_request()
    {
        _services.Setup(p => p.EnsureUsableAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Service is inactive"));
        var o = await Sut().ScheduleAsync(_clientId, _leadId, new ScheduleConsultationRequest(DateTimeOffset.UtcNow.AddDays(3), ServiceId: Guid.NewGuid()));
        Assert.Equal(("INVALID_REQUEST", "Service is inactive"), (o.Code, o.Message));
    }

    [Fact]
    public async Task CancelConsultation_outcomes()
    {
        var none = await Sut().CancelConsultationAsync(_clientId, _leadId, null, null);
        Assert.Equal("NO_UPCOMING_APPOINTMENT", none.Code);

        var a1 = MakeAppointment();
        Setup(a1);
        var one = await Sut().CancelConsultationAsync(_clientId, _leadId, null, "r");
        Assert.Equal(("canceled", "CANCELED"), (one.Operation, one.Code));
        Assert.Equal(a1.Id, one.Appointment!.Id);

        var a2 = MakeAppointment(daysAhead: 4);
        var a3 = MakeAppointment(daysAhead: 5);
        _upcoming.Clear();
        Setup(a2);
        Setup(a3);
        var multiple = await Sut().CancelConsultationAsync(_clientId, _leadId, null, null);
        Assert.Equal("MULTIPLE_UPCOMING_APPOINTMENTS", multiple.Code);
        Assert.Equal(2, multiple.Existing!.Count);

        var invalid = await Sut().CancelConsultationAsync(_clientId, _leadId, Guid.NewGuid(), null);
        Assert.Equal("INVALID_REQUEST", invalid.Code);

        var specific = await Sut().CancelConsultationAsync(_clientId, _leadId, a3.Id, null);
        Assert.Equal("CANCELED", specific.Code);
        Assert.Equal(AppointmentStatus.Canceled, a3.Status);
        Assert.Equal(AppointmentStatus.Booked, a2.Status);
    }

    [Fact]
    public async Task CancelConsultation_ignores_the_id_when_exactly_one_is_upcoming()
    {
        // By design (see the comment in CancelConsultationAsync): the AI's appointment ids go stale, so with one upcoming
        // appointment the backend selects it itself. This was reported as BUG-03 and withdrawn.
        var a = MakeAppointment();
        Setup(a);
        var o = await Sut().CancelConsultationAsync(_clientId, _leadId, Guid.NewGuid(), null);
        Assert.Equal("CANCELED", o.Code);
        Assert.Equal(AppointmentStatus.Canceled, a.Status);
    }

    // ---------------------------------------------------------------- queries

    [Fact]
    public async Task GetUpcomingForLead_converts_times_to_client_zone()
    {
        _client.Timezone = "Asia/Beirut";
        var a = MakeAppointment();
        Setup(a);
        var r = await Sut().GetUpcomingForLeadAsync(_clientId, _leadId);
        Assert.Equal("Asia/Beirut", r.Timezone);
        var item = Assert.Single(r.Appointments);
        var local = TimeZoneInfo.ConvertTime(a.ScheduledStart, TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut"));
        Assert.Equal((local.ToString("yyyy-MM-dd"), local.ToString("HH:mm")), (item.Date, item.Time));
        Assert.Contains(" at ", item.Label);
    }

    [Fact]
    public async Task Unknown_client_or_bad_timezone_falls_back_to_utc()
    {
        _client.Timezone = "Nope/Zone";
        Assert.Equal("UTC", (await Sut().GetUpcomingForLeadAsync(_clientId, _leadId)).Timezone);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync((Client?)null);
        Assert.Equal("UTC", (await Sut().GetUpcomingForLeadAsync(_clientId, _leadId)).Timezone);
    }

    [Fact]
    public async Task GetById_and_List_map_through_the_mapper()
    {
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
        var a = MakeAppointment();
        Setup(a);
        Assert.Equal(a.Id, (await Sut().GetByIdAsync(_clientId, a.Id))!.Id);
        _appointments.Setup(r => r.ListAsync(_clientId, "booked", null, null, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Appointment> { a } as IReadOnlyList<Appointment>, 7));
        var (items, total) = await Sut().ListAsync(_clientId, "booked", null, null, 0, 10);
        Assert.Equal((1, 7), (items.Count, total));
    }

    [Fact]
    public async Task CalendarMonth_builds_a_sunday_aligned_grid()
    {
        var a = MakeAppointment();
        _appointments.Setup(r => r.ListStartingBetweenAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { a });
        _appointments.Setup(r => r.CountNeedingOutcomeAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var m = await Sut().GetCalendarMonthAsync(_clientId, 2026, 10);

        Assert.Equal("2026-09-27", m.GridStart); // 1 Oct 2026 is a Thursday
        Assert.Equal("2026-10-31", m.GridEnd);   // 31 Oct 2026 is a Saturday
        Assert.Equal(4, m.NeedsOutcomeCount);
        Assert.Equal("Ann Lee", Assert.Single(m.Items).LeadFullName);
        _appointments.Verify(r => r.ListStartingBetweenAsync(_clientId,
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CalendarMonth_display_zone_shifts_bounds_and_times()
    {
        var beirut = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut");
        _client.Timezone = "UTC";
        _appointments.Setup(r => r.ListStartingBetweenAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        var m = await Sut().GetCalendarMonthAsync(_clientId, 2026, 1, beirut);
        Assert.Equal(("Asia/Beirut", "UTC"), (m.Timezone, m.ClientTimezone));
        _appointments.Verify(r => r.ListStartingBetweenAsync(_clientId, new DateTimeOffset(2025, 12, 28, 0, 0, 0, TimeSpan.FromHours(2)).ToUniversalTime(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

