using Omni.App.Business.Services.Appointments;
using Omni.App.Entities.Dtos.Appointments;

namespace Omni.App.Tests.Services;

public class AvailabilityServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IAvailabilityRepository> _availability = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IServiceRepository> _services = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Client _client;
    private readonly List<AvailabilityRule> _rules = new();
    private BookingSettings? _settings;
    private readonly List<AvailabilityException> _exceptions = new();
    private readonly List<BusyAppointmentRow> _busy = new();

    public AvailabilityServiceTests()
    {
        _client = new Client { Id = _clientId, Timezone = "UTC" };
        _config.SetupGet(c => c.AvailabilityDefaultDurationMinutes).Returns(30);
        _config.SetupGet(c => c.AvailabilityDefaultBufferMinutes).Returns(0);
        _config.SetupGet(c => c.AvailabilityDefaultNoticeMinutes).Returns(0);
        _config.SetupGet(c => c.AvailabilityDefaultHorizonDays).Returns(60);
        _config.SetupGet(c => c.AvailabilityMaxRangeDays).Returns(14);
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _client);
        _clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _client);
        _availability.Setup(a => a.ListRulesReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _rules);
        _availability.Setup(a => a.ListRulesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _rules);
        _availability.Setup(a => a.GetBookingSettingsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        _availability.Setup(a => a.GetBookingSettingsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _settings);
        _availability.Setup(a => a.ListExceptionsAsync(_clientId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly f, DateOnly t, CancellationToken _) => _exceptions.Where(e => e.Date >= f && e.Date <= t).ToList());
        _availability.Setup(a => a.ListExceptionsFromAsync(_clientId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly f, CancellationToken _) => _exceptions.Where(e => e.Date >= f).ToList());
        _appointments.Setup(a => a.ListBusyAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _busy.ToList());
    }

    private AvailabilityService Sut() =>
        new(_availability.Object, _clients.Object, _services.Object, _appointments.Object, _uow.Object, _config.Object);

    private void OpenEveryDay(string start = "09:00", string end = "17:00")
    {
        for (var d = 0; d < 7; d++)
            _rules.Add(new AvailabilityRule { Id = Guid.NewGuid(), ClientId = _clientId, DayOfWeek = d, IsOpen = true, StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end) });
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---------------------------------------------------------------- GetSlots

    [Fact]
    public async Task Unknown_client_gives_not_configured_response()
    {
        var r = await Sut().GetSlotsAsync(Guid.NewGuid(), null, null, 3);
        Assert.False(r.Configured);
        Assert.Equal("Client not found.", r.Message);
        Assert.Empty(r.Slots);
    }

    [Fact]
    public async Task Client_without_open_days_is_not_configured()
    {
        var r = await Sut().GetSlotsAsync(_clientId, null, null, 3);
        Assert.False(r.Configured);
        Assert.Contains("hasn't set up", r.Message);
    }

    [Fact]
    public async Task Past_date_is_explained_not_searched()
    {
        OpenEveryDay();
        var r = await Sut().GetSlotsAsync(_clientId, null, Today.AddDays(-2), 3);
        Assert.True(r.Configured);
        Assert.Empty(r.Slots);
        Assert.Contains("is in the past", r.Message);
    }

    [Fact]
    public async Task Start_beyond_the_booking_horizon_is_rejected()
    {
        OpenEveryDay();
        _settings = new BookingSettings { DefaultConsultationDurationMinutes = 30, MaximumAdvanceBookingDays = 10 };
        var r = await Sut().GetSlotsAsync(_clientId, null, Today.AddDays(11), 3);
        Assert.Empty(r.Slots);
        Assert.Contains("up to 10 days ahead", r.Message);
    }

    [Fact]
    public async Task Slots_follow_the_grid_and_stop_at_closing_time()
    {
        OpenEveryDay("09:00", "10:30");
        var day = Today.AddDays(2);
        var r = await Sut().GetSlotsAsync(_clientId, null, day, 1);
        Assert.Equal(["09:00", "09:30", "10:00"], r.Slots.Select(s => s.Time));
        Assert.All(r.Slots, s => Assert.Equal(day.ToString("yyyy-MM-dd"), s.Date));
        Assert.Equal(TimeSpan.FromMinutes(30), r.Slots[0].End - r.Slots[0].Start);
        Assert.Null(r.Message);
    }

    [Fact]
    public async Task Buffer_widens_the_step_and_range_is_clamped()
    {
        OpenEveryDay("09:00", "11:00");
        _settings = new BookingSettings { DefaultConsultationDurationMinutes = 30, BufferMinutes = 15, MaximumAdvanceBookingDays = 60 };
        var r = await Sut().GetSlotsAsync(_clientId, null, Today.AddDays(1), 1000);
        Assert.Equal(["09:00", "09:45", "10:30"], r.Slots.Where(s => s.Date == Today.AddDays(1).ToString("yyyy-MM-dd")).Select(s => s.Time));
        Assert.Equal(Today.AddDays(1 + 13).ToString("yyyy-MM-dd"), r.To); // MaxRangeDays = 14
    }

    [Fact]
    public async Task Existing_appointments_block_slots_and_the_search_jumps_past_them()
    {
        OpenEveryDay("09:00", "12:00");
        var day = Today.AddDays(2);
        _busy.Add(new BusyAppointmentRow(new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 10)), TimeSpan.Zero), new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 40)), TimeSpan.Zero), null));
        var r = await Sut().GetSlotsAsync(_clientId, null, day, 1);
        Assert.Equal(["09:40", "10:10", "10:40", "11:10"], r.Slots.Select(s => s.Time));
    }

    [Fact]
    public async Task Busy_row_without_end_uses_service_minutes_then_default()
    {
        OpenEveryDay("09:00", "11:00");
        var day = Today.AddDays(2);
        _busy.Add(new BusyAppointmentRow(new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), null, 60));
        var r = await Sut().GetSlotsAsync(_clientId, null, day, 1);
        Assert.Equal(["10:00", "10:30"], r.Slots.Select(s => s.Time));
    }

    [Fact]
    public async Task Closed_exception_removes_the_day_and_special_hours_replace_the_rules()
    {
        OpenEveryDay("09:00", "10:00");
        var d1 = Today.AddDays(2);
        var d2 = Today.AddDays(3);
        _exceptions.Add(new AvailabilityException { Date = d1, IsClosed = true });
        _exceptions.Add(new AvailabilityException { Date = d2, IsClosed = false, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0) });
        var r = await Sut().GetSlotsAsync(_clientId, null, d1, 2);
        Assert.Equal(["14:00", "14:30"], r.Slots.Select(s => s.Time));
        Assert.All(r.Slots, s => Assert.Equal(d2.ToString("yyyy-MM-dd"), s.Date));
    }

    [Fact]
    public async Task Minimum_notice_hides_early_slots_today()
    {
        OpenEveryDay("00:00", "23:59");
        _settings = new BookingSettings { DefaultConsultationDurationMinutes = 30, MinimumBookingNoticeMinutes = 60 * 48, MaximumAdvanceBookingDays = 60 };
        var r = await Sut().GetSlotsAsync(_clientId, null, Today, 1);
        Assert.Empty(r.Slots);
        Assert.Equal("No available times in this date range.", r.Message);
    }

    [Fact]
    public async Task Service_duration_overrides_default_and_bad_services_are_rejected()
    {
        OpenEveryDay("09:00", "11:00");
        var svc = new Service { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Rhino", IsActive = true, ConsultationDuration = 60 };
        _services.Setup(p => p.GetReadOnlyAsync(_clientId, svc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(svc);
        var r = await Sut().GetSlotsAsync(_clientId, svc.Id, Today.AddDays(2), 1);
        Assert.Equal(60, r.DurationMinutes);
        Assert.Equal(["09:00", "10:00"], r.Slots.Select(s => s.Time));

        svc.ConsultationDuration = null;
        Assert.Equal(30, (await Sut().GetSlotsAsync(_clientId, svc.Id, Today.AddDays(2), 1)).DurationMinutes);

        svc.IsActive = false;
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetSlotsAsync(_clientId, svc.Id, null, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetSlotsAsync(_clientId, Guid.NewGuid(), null, 1));
    }

    [Fact]
    public async Task Client_timezone_shifts_slots_to_local_hours()
    {
        _client.Timezone = "Asia/Beirut";
        OpenEveryDay("09:00", "10:00");
        var r = await Sut().GetSlotsAsync(_clientId, null, Today.AddDays(3), 1);
        Assert.Equal("Asia/Beirut", r.Timezone);
        Assert.Equal(["09:00", "09:30"], r.Slots.Select(s => s.Time));
        var offset = TimeZoneInfo.FindSystemTimeZoneById("Asia/Beirut").GetUtcOffset(r.Slots[0].Start.UtcDateTime);
        Assert.Equal(offset, r.Slots[0].Start.Offset);
    }

    [Fact]
    public async Task Invalid_client_timezone_falls_back_to_utc()
    {
        _client.Timezone = "Mars/Olympus";
        OpenEveryDay();
        Assert.Equal("UTC", (await Sut().GetSlotsAsync(_clientId, null, Today.AddDays(1), 1)).Timezone);
    }

    // ---------------------------------------------------------------- CheckSlot

    private DateTimeOffset At(int daysAhead, int hour, int minute = 0) =>
        new(Today.AddDays(daysAhead).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    [Fact]
    public async Task CheckSlot_accepts_a_free_slot_inside_hours()
    {
        OpenEveryDay();
        var c = await Sut().CheckSlotAsync(_clientId, null, At(3, 10), null);
        Assert.Null(c.Error);
        Assert.Equal(At(3, 10, 30), c.End);
    }

    [Fact]
    public async Task CheckSlot_rejects_in_all_the_documented_ways()
    {
        Assert.Equal("Client not found.", (await Sut().CheckSlotAsync(Guid.NewGuid(), null, At(3, 10), null)).Error);
        Assert.Contains("hasn't set up", (await Sut().CheckSlotAsync(_clientId, null, At(3, 10), null)).Error);

        OpenEveryDay();
        Assert.Contains("must end after", (await Sut().CheckSlotAsync(_clientId, null, At(3, 10), At(3, 9))).Error);
        Assert.Contains("too soon", (await Sut().CheckSlotAsync(_clientId, null, DateTimeOffset.UtcNow.AddMinutes(-5), null)).Error);
        Assert.Contains("days ahead", (await Sut().CheckSlotAsync(_clientId, null, At(400, 10), null)).Error);
        Assert.Contains("not open", (await Sut().CheckSlotAsync(_clientId, null, At(3, 7), null)).Error);
        Assert.Contains("not open", (await Sut().CheckSlotAsync(_clientId, null, At(3, 16, 45), null)).Error);
    }

    [Fact]
    public async Task CheckSlot_detects_overlap_but_can_exclude_the_appointment_being_moved()
    {
        OpenEveryDay();
        _busy.Add(new BusyAppointmentRow(At(3, 10), At(3, 10, 30), null));
        Assert.Contains("overlaps", (await Sut().CheckSlotAsync(_clientId, null, At(3, 10, 15), null)).Error);
        Assert.Null((await Sut().CheckSlotAsync(_clientId, null, At(3, 10, 30), null)).Error);

        var excluded = Guid.NewGuid();
        _appointments.Setup(a => a.ListBusyAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), excluded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BusyAppointmentRow>());
        Assert.Null((await Sut().CheckSlotAsync(_clientId, null, At(3, 10, 15), null, excluded)).Error);
    }

    [Fact]
    public async Task CheckSlot_respects_buffer_and_closed_exceptions()
    {
        OpenEveryDay();
        _settings = new BookingSettings { DefaultConsultationDurationMinutes = 30, BufferMinutes = 15, MaximumAdvanceBookingDays = 60 };
        _busy.Add(new BusyAppointmentRow(At(3, 10), At(3, 10, 30), null));
        Assert.NotNull((await Sut().CheckSlotAsync(_clientId, null, At(3, 10, 30), null)).Error);
        Assert.Null((await Sut().CheckSlotAsync(_clientId, null, At(3, 10, 45), null)).Error);

        _exceptions.Add(new AvailabilityException { Date = Today.AddDays(4), IsClosed = true });
        Assert.Contains("not open", (await Sut().CheckSlotAsync(_clientId, null, At(4, 10), null)).Error);
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public async Task GetSettings_fills_missing_days_with_defaults_and_lists_future_exceptions()
    {
        _rules.Add(new AvailabilityRule { DayOfWeek = 1, IsOpen = true, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 30) });
        _exceptions.Add(new AvailabilityException { Id = Guid.NewGuid(), Date = Today.AddDays(5), IsClosed = false, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), Reason = "Short day" });
        var s = await Sut().GetSettingsAsync(_clientId);
        Assert.True(s.Configured);
        Assert.Equal(7, s.Days.Count);
        Assert.Equal(new DayRuleDto(1, true, "08:00", "12:30"), s.Days[1]);
        Assert.Equal(new DayRuleDto(0, false, "09:00", "17:00"), s.Days[0]);
        Assert.Equal(30, s.Booking.DefaultDurationMinutes);
        var e = Assert.Single(s.Exceptions);
        Assert.Equal(("10:00", "11:00", "Short day"), (e.Start, e.End, e.Reason));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().GetSettingsAsync(Guid.NewGuid()));
    }

    private static List<DayRuleDto> Week(Func<int, DayRuleDto>? custom = null) =>
        Enumerable.Range(0, 7).Select(d => custom?.Invoke(d) ?? new DayRuleDto(d, d is >= 1 and <= 5, "09:00", "17:00")).ToList();

    private static readonly BookingRulesDto GoodRules = new(30, 10, 120, 90);

    [Fact]
    public async Task SaveSchedule_creates_rules_settings_and_updates_the_client()
    {
        var added = new List<AvailabilityRule>();
        _availability.Setup(a => a.AddRule(It.IsAny<AvailabilityRule>())).Callback<AvailabilityRule>(added.Add);
        _availability.Setup(a => a.AddBookingSettings(It.IsAny<BookingSettings>())).Callback<BookingSettings>(s => _settings = s);

        await Sut().SaveScheduleAsync(_clientId, " Asia/Beirut ", Week(), GoodRules);

        Assert.Equal(7, added.Count);
        Assert.Equal(5, added.Count(r => r.IsOpen));
        Assert.Equal("Asia/Beirut", _client.Timezone);
        Assert.Equal((30, 10, 120, 90), (_settings!.DefaultConsultationDurationMinutes, _settings.BufferMinutes, _settings.MinimumBookingNoticeMinutes, _settings.MaximumAdvanceBookingDays));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSchedule_updates_existing_rules_and_normalises_bad_closed_days()
    {
        var monday = new AvailabilityRule { DayOfWeek = 1, IsOpen = false, StartTime = new TimeOnly(1, 0), EndTime = new TimeOnly(2, 0) };
        _rules.Add(monday);
        _settings = new BookingSettings();
        var week = Week(d => d == 1 ? new DayRuleDto(1, true, "08:15", "13:00") : new DayRuleDto(d, false, "garbage", "x"));
        await Sut().SaveScheduleAsync(_clientId, "UTC", week, GoodRules);
        Assert.True(monday.IsOpen);
        Assert.Equal(new TimeOnly(8, 15), monday.StartTime);
        _availability.Verify(a => a.AddRule(It.Is<AvailabilityRule>(r => !r.IsOpen && r.StartTime == new TimeOnly(9, 0) && r.EndTime == new TimeOnly(17, 0))), Times.Exactly(6));
        _availability.Verify(a => a.AddBookingSettings(It.IsAny<BookingSettings>()), Times.Never);
    }

    [Theory]
    [InlineData("Mars/Olympus", 30, 0, 0, 30)]
    [InlineData("UTC", 4, 0, 0, 30)]
    [InlineData("UTC", 481, 0, 0, 30)]
    [InlineData("UTC", 30, -1, 0, 30)]
    [InlineData("UTC", 30, 241, 0, 30)]
    [InlineData("UTC", 30, 0, -1, 30)]
    [InlineData("UTC", 30, 0, 43201, 30)]
    [InlineData("UTC", 30, 0, 0, 0)]
    [InlineData("UTC", 30, 0, 0, 366)]
    public async Task SaveSchedule_validates_booking_rules(string tz, int dur, int buffer, int notice, int advance) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(_clientId, tz, Week(), new BookingRulesDto(dur, buffer, notice, advance)));

    [Fact]
    public async Task SaveSchedule_validates_days()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(Guid.NewGuid(), "UTC", Week(), GoodRules));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(_clientId, "UTC", [new DayRuleDto(7, true, "09:00", "10:00")], GoodRules));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(_clientId, "UTC", [new DayRuleDto(1, true, "", "10:00")], GoodRules));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(_clientId, "UTC", [new DayRuleDto(1, true, "10:00", "10:00")], GoodRules));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveScheduleAsync(_clientId, "UTC", [new DayRuleDto(1, true, "09:00", "10:00"), new DayRuleDto(1, false, "09:00", "10:00")], GoodRules));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveException_creates_or_updates_and_validates()
    {
        var day = Today.AddDays(9);
        AvailabilityException? added = null;
        _availability.Setup(a => a.AddException(It.IsAny<AvailabilityException>())).Callback<AvailabilityException>(e => added = e);
        await Sut().SaveExceptionAsync(_clientId, day, false, "10:00", "12:00", "  Event  ");
        Assert.Equal((new TimeOnly(10, 0), new TimeOnly(12, 0), "Event", false), (added!.StartTime, added.EndTime, added.Reason, added.IsClosed));

        var existing = new AvailabilityException { Date = day, StartTime = new TimeOnly(1, 0), EndTime = new TimeOnly(2, 0) };
        _availability.Setup(a => a.GetExceptionByDateAsync(_clientId, day, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        await Sut().SaveExceptionAsync(_clientId, day, true, "ignored", null, " ");
        Assert.True(existing.IsClosed);
        Assert.Null(existing.StartTime);
        Assert.Null(existing.Reason);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveExceptionAsync(_clientId, day, false, null, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveExceptionAsync(_clientId, day, false, "12:00", "10:00", null));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveExceptionAsync(_clientId, day, true, null, null, new string('x', 201)));
    }

    [Fact]
    public async Task DeleteException_removes_only_when_found()
    {
        var id = Guid.NewGuid();
        await Sut().DeleteExceptionAsync(_clientId, id);
        _availability.Verify(a => a.RemoveException(It.IsAny<AvailabilityException>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var ex = new AvailabilityException { Id = id };
        _availability.Setup(a => a.GetExceptionAsync(_clientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(ex);
        await Sut().DeleteExceptionAsync(_clientId, id);
        _availability.Verify(a => a.RemoveException(ex), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

