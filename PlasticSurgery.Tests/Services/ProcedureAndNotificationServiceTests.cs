using PlasticSurgery.Business.Services.Notifications;
using PlasticSurgery.Business.Services.Procedures;

namespace PlasticSurgery.Tests.Services;

public class ProcedureServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IProcedureRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private ProcedureService Sut() => new(_repo.Object, _uow.Object);

    private Procedure Existing(bool active = true)
    {
        var p = new Procedure { Id = Guid.NewGuid(), ClinicId = _clinicId, Name = "Rhinoplasty", IsActive = active };
        _repo.Setup(r => r.GetAsync(_clinicId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p);
        return p;
    }

    [Fact]
    public async Task List_and_GetById_map()
    {
        var p = Existing();
        _repo.Setup(r => r.ListAsync(_clinicId, true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Procedure> { p });
        Assert.Single(await Sut().ListAsync(_clinicId, true));
        Assert.Equal(p.Id, (await Sut().GetByIdAsync(_clinicId, p.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clinicId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Create_trims_and_saves_an_active_procedure()
    {
        Procedure? added = null;
        _repo.Setup(r => r.Add(It.IsAny<Procedure>())).Callback<Procedure>(p => added = p);
        var r = await Sut().CreateAsync(new CreateProcedureRequest(_clinicId, "  Facelift ", " FL ", "  ", 45));
        Assert.Equal(("Facelift", "FL", null, 45, true), (added!.Name, added.Code, added.Description, added.ConsultationDuration, added.IsActive));
        Assert.Equal("Facelift", r.Name);
    }

    [Fact]
    public async Task Create_rejects_duplicates_in_the_same_clinic()
    {
        _repo.Setup(r => r.NameExistsAsync(_clinicId, "Facelift", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureRequest(_clinicId, "Facelift", null, null, null)));
    }

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("   ", null, null, null)]
    [InlineData("ok", null, null, 0)]
    [InlineData("ok", null, null, 481)]
    public async Task Create_validates_inputs(string name, string? code, string? description, int? duration) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureRequest(_clinicId, name, code, description, duration)));

    [Fact]
    public async Task Create_validates_lengths()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureRequest(_clinicId, new string('n', 201), null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureRequest(_clinicId, "n", new string('c', 101), null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureRequest(_clinicId, "n", null, new string('d', 501), null)));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_changes_fields_and_excludes_itself_from_the_duplicate_check()
    {
        var p = Existing();
        var r = await Sut().UpdateAsync(_clinicId, p.Id, new UpdateProcedureRequest("Nose job", "NJ", "desc", 30));
        Assert.Equal(("Nose job", "NJ", "desc", 30), (p.Name, p.Code, p.Description, p.ConsultationDuration));
        _repo.Verify(x => x.NameExistsAsync(_clinicId, "Nose job", p.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Nose job", r!.Name);
        Assert.Null(await Sut().UpdateAsync(_clinicId, Guid.NewGuid(), new UpdateProcedureRequest("x", null, null, null)));
        _repo.Setup(x => x.NameExistsAsync(_clinicId, "Dup", p.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clinicId, p.Id, new UpdateProcedureRequest("Dup", null, null, null)));
    }

    [Fact]
    public async Task SetActive_toggles()
    {
        var p = Existing();
        Assert.False((await Sut().SetActiveAsync(_clinicId, p.Id, false))!.IsActive);
        Assert.Null(await Sut().SetActiveAsync(_clinicId, Guid.NewGuid(), true));
    }

    [Fact]
    public async Task EnsureUsable_accepts_only_active_procedures_of_the_clinic()
    {
        var active = Existing();
        await Sut().EnsureUsableAsync(_clinicId, active.Id);
        var inactive = Existing(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().EnsureUsableAsync(_clinicId, inactive.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().EnsureUsableAsync(_clinicId, Guid.NewGuid()));
    }
}

public class ProcedureBookingServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IProcedureBookingRepository> _bookings = new();
    private readonly Mock<ILeadRepository> _leads = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<IProcedureService> _procedures = new();

    public ProcedureBookingServiceTests() =>
        _leads.Setup(l => l.ExistsAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private ProcedureBookingService Sut() => new(_bookings.Object, _leads.Object, _uow.Object, _events.Object, _procedures.Object);

    private ProcedureBooking Booking(string status = ProcedureBookingStatus.Considering, Guid? leadId = null)
    {
        var b = new ProcedureBooking { Id = Guid.NewGuid(), ClinicId = _clinicId, LeadId = leadId ?? Guid.NewGuid(), ProcedureId = Guid.NewGuid(), Status = status };
        _bookings.Setup(r => r.GetAsync(_clinicId, b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        return b;
    }

    [Fact]
    public async Task Create_checks_the_procedure_and_starts_as_considering()
    {
        ProcedureBooking? added = null;
        _bookings.Setup(r => r.Add(It.IsAny<ProcedureBooking>())).Callback<ProcedureBooking>(b => added = b);
        var leadId = Guid.NewGuid();
        var procId = Guid.NewGuid();
        await Sut().CreateAsync(new CreateProcedureBookingRequest(_clinicId, leadId, procId, null, 1200m, "USD", "n"));
        Assert.Equal((ProcedureBookingStatus.Considering, 1200m, "USD", leadId), (added!.Status, added.QuotedAmount, added.CurrencyCode, added.LeadId));
        _procedures.Verify(p => p.EnsureUsableAsync(_clinicId, procId, It.IsAny<CancellationToken>()), Times.Once);
        _bookings.Verify(b => b.LoadDetailsAsync(added, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_with_an_unusable_procedure_saves_nothing()
    {
        _procedures.Setup(p => p.EnsureUsableAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("inactive"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureBookingRequest(_clinicId, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null)));
        _bookings.Verify(r => r.Add(It.IsAny<ProcedureBooking>()), Times.Never);
    }

    [Fact]
    public async Task Create_rejects_a_lead_from_another_clinic()
    {
        _leads.Setup(l => l.ExistsAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(new CreateProcedureBookingRequest(_clinicId, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null)));
    }

    [Fact]
    public async Task Update_applies_only_supplied_fields()
    {
        var b = Booking();
        b.QuotedAmount = 100m;
        var date = DateTimeOffset.UtcNow.AddDays(30);
        await Sut().UpdateAsync(_clinicId, b.Id, new UpdateProcedureBookingRequest(ProcedureBookingStatus.Quoted, null, 50m, 90m, "EUR", date, "note"));
        Assert.Equal((ProcedureBookingStatus.Quoted, 100m, 50m, 90m, "EUR", date, "note"), (b.Status, b.QuotedAmount, b.DepositAmount, b.FinalAmount, b.CurrencyCode, b.ProcedureDate, b.Notes));
        _events.Verify(e => e.Log(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Update_validates_status_and_missing_booking()
    {
        Assert.Null(await Sut().UpdateAsync(_clinicId, Guid.NewGuid(), new UpdateProcedureBookingRequest(null, null, null, null, null, null, null)));
        var b = Booking();
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clinicId, b.Id, new UpdateProcedureBookingRequest("bogus", null, null, null, null, null, null)));
    }

    [Fact]
    public async Task Completing_a_booking_marks_the_lead_and_logs_the_amount_invariantly()
    {
        var lead = new Lead { Id = Guid.NewGuid(), Status = LeadStatus.ConsultationAttended };
        _leads.Setup(l => l.GetByIdAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var b = Booking(leadId: lead.Id);
        await Sut().UpdateAsync(_clinicId, b.Id, new UpdateProcedureBookingRequest(ProcedureBookingStatus.Completed, null, null, 1234.5m, null, null, null));
        Assert.Equal(LeadStatus.SurgeryBooked, lead.Status);
        _events.Verify(e => e.Log(_clinicId, EventTypes.ProcedureBooked, lead.Id, null, null, null, "{\"amount\": 1234.5}"), Times.Once);
    }

    [Fact]
    public async Task Completing_without_a_final_amount_logs_null()
    {
        var b = Booking();
        await Sut().UpdateAsync(_clinicId, b.Id, new UpdateProcedureBookingRequest(ProcedureBookingStatus.Completed, null, null, null, null, null, null));
        _events.Verify(e => e.Log(_clinicId, EventTypes.ProcedureBooked, b.LeadId, null, null, null, "{\"amount\": null}"), Times.Once);
    }

    [Fact(Skip = "BUG-07: the lead only becomes 'surgery_booked' (and procedure_booked is only logged) when the booking is marked Completed, not when it is marked Booked/DepositPaid - confirm intended behaviour with the product owner")]
    public async Task Marking_a_booking_as_booked_moves_the_lead_to_surgery_booked()
    {
        var lead = new Lead { Id = Guid.NewGuid(), Status = LeadStatus.ConsultationAttended };
        _leads.Setup(l => l.GetByIdAsync(lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var b = Booking(leadId: lead.Id);
        await Sut().UpdateAsync(_clinicId, b.Id, new UpdateProcedureBookingRequest(ProcedureBookingStatus.Booked, null, null, null, null, null, null));
        Assert.Equal(LeadStatus.SurgeryBooked, lead.Status);
    }

    [Fact]
    public async Task List_maps_rows()
    {
        var b = Booking();
        b.Procedure = new Procedure { Name = "Rhino" };
        _bookings.Setup(r => r.ListAsync(_clinicId, "quoted", 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ProcedureBooking> { b } as IReadOnlyList<ProcedureBooking>, 3));
        var (items, total) = await Sut().ListAsync(_clinicId, "quoted", 0, 10);
        Assert.Equal((1, 3), (items.Count, total));
    }
}

public class NotificationServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();

    private NotificationService Sut() => new(_repo.Object, _uow.Object, _notifier.Object);

    [Fact]
    public async Task Create_saves_and_pushes_live()
    {
        Notification? added = null;
        _repo.Setup(r => r.Add(It.IsAny<Notification>())).Callback<Notification>(n => added = n);
        var lead = Guid.NewGuid();
        await Sut().CreateAsync(_clinicId, NotificationType.NewLead, "New lead", "msg", leadId: lead, link: "/x");
        Assert.Equal((NotificationType.NewLead, "New lead", lead, false, "/x"), (added!.Type, added.Title, added.LeadId, added.IsRead, added.Link));
        _notifier.Verify(n => n.NotificationCreatedAsync(_clinicId, It.Is<NotificationResponse>(r => r.Id == added.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_never_throws_even_when_saving_or_pushing_fails()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await Sut().CreateAsync(_clinicId, "t", "title", null);
        _notifier.Verify(n => n.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _notifier.Setup(n => n.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationResponse>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("hub"));
        await Sut().CreateAsync(_clinicId, "t", "title", null);
    }

    [Fact]
    public async Task List_returns_counts_and_unread()
    {
        _repo.Setup(r => r.ListAsync(_clinicId, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Notification> { new() { Id = Guid.NewGuid(), Title = "a" } } as IReadOnlyList<Notification>, 7, 3));
        var r = await Sut().ListAsync(_clinicId, 0, 10);
        Assert.Equal((1, 3, 7), (r.Items.Count, r.UnreadCount, r.TotalCount));
        _repo.Setup(x => x.CountUnreadAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        Assert.Equal(4, await Sut().GetUnreadCountAsync(_clinicId));
    }

    [Fact]
    public async Task MarkRead_ignores_missing_or_already_read_and_stamps_unread()
    {
        await Sut().MarkReadAsync(_clinicId, Guid.NewGuid());
        var read = new Notification { Id = Guid.NewGuid(), IsRead = true };
        _repo.Setup(r => r.GetAsync(_clinicId, read.Id, It.IsAny<CancellationToken>())).ReturnsAsync(read);
        await Sut().MarkReadAsync(_clinicId, read.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var unread = new Notification { Id = Guid.NewGuid(), IsRead = false };
        _repo.Setup(r => r.GetAsync(_clinicId, unread.Id, It.IsAny<CancellationToken>())).ReturnsAsync(unread);
        await Sut().MarkReadAsync(_clinicId, unread.Id);
        Assert.True(unread.IsRead);
        Assert.NotNull(unread.ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_delegates()
    {
        await Sut().MarkAllReadAsync(_clinicId);
        _repo.Verify(r => r.MarkAllReadAsync(_clinicId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
