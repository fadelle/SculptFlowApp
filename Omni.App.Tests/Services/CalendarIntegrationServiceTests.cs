using Omni.App.Business.Services.Calendars;

namespace Omni.App.Tests.Services;

public class CalendarIntegrationServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<ICalendarIntegrationRepository> _calendars = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICalendarSyncNotifier> _notifier = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICalendarProviderClient> _google = new();
    private readonly Mock<IConfigManager> _config = new();
    private CalendarIntegration? _row;
    private readonly Mock<IEntitlementService> _entitlements = new();

    public CalendarIntegrationServiceTests()
    {
        _google.SetupGet(g => g.Provider).Returns(CalendarProvider.Google);
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        _config.SetupGet(c => c.IntegrationsTokenRefreshMarginMinutes).Returns(5);
        _calendars.Setup(c => c.GetWithCalendarsAsync(_clientId, CalendarProvider.Google, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _calendars.Setup(c => c.GetByIdAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _calendars.Setup(c => c.Add(It.IsAny<CalendarIntegration>())).Callback<CalendarIntegration>(r => _row = r);
        _calendars.Setup(c => c.ReplaceCalendars(It.IsAny<CalendarIntegration>(), It.IsAny<IEnumerable<CalendarIntegrationCalendar>>()))
            .Callback<CalendarIntegration, IEnumerable<CalendarIntegrationCalendar>>((r, cals) => { r.Calendars.Clear(); foreach (var c in cals) r.Calendars.Add(c); });
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Timezone = "Asia/Beirut" });
    }

    private CalendarIntegrationService Sut() => new(_calendars.Object, _clients.Object, _uow.Object, _notifier.Object, _notifications.Object, [_google.Object], _config.Object, _entitlements.Object);

    private CalendarIntegration Connected(string? selected = "cal1", bool sync = true) => _row = new CalendarIntegration
    {
        Id = Guid.NewGuid(), ClientId = _clientId, Provider = CalendarProvider.Google, Status = CalendarIntegrationStatus.Connected,
        AccessToken = "access", RefreshToken = "refresh", TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        SelectedCalendarId = selected, SelectedCalendarName = selected, SyncEnabled = sync, IsHealthy = true,
        Calendars = { new CalendarIntegrationCalendar { ExternalCalendarId = "cal1", Name = "Main", IsPrimary = true } }
    };

    private static BookingResponse Appt(Guid? id = null) => new(id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Ann", null, null,
        "standard", "booked", DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddMinutes(30), null, "Room 1", null, DateTimeOffset.UtcNow);

    // ---------------------------------------------------------------- list / connect

    [Fact]
    public async Task List_returns_every_provider_with_disconnected_placeholders()
    {
        Connected();
        _calendars.Setup(c => c.ListWithCalendarsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { _row! });
        var list = await Sut().ListAsync(_clientId);
        Assert.Equal(CalendarProvider.All.Count, list.Count);
        Assert.Equal(CalendarIntegrationStatus.Connected, list.Single(l => l.Provider == CalendarProvider.Google).Status);
        var outlook = list.Single(l => l.Provider == CalendarProvider.Outlook);
        Assert.Equal((Guid.Empty, CalendarIntegrationStatus.Disconnected), (outlook.Id, outlook.Status));
    }

    [Fact]
    public async Task RequestConnect_creates_a_pending_row_and_rejects_unknown_providers()
    {
        var r = await Sut().RequestConnectAsync(_clientId, CalendarProvider.Google);
        Assert.Equal(CalendarIntegrationStatus.Pending, r.Status);
        _calendars.Verify(c => c.Add(It.IsAny<CalendarIntegration>()), Times.Once);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RequestConnectAsync(_clientId, "yahoo"));
    }

    [Fact]
    public async Task CompleteConnect_stores_tokens_replaces_calendars_and_clears_a_stale_selection()
    {
        Connected(selected: "gone");
        _google.Setup(g => g.ListCalendarsAsync("new-access", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarCalendarOption> { new("cal9", "Work", true) });
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var r = await Sut().CompleteConnectAsync(_clientId, CalendarProvider.Google, new CalendarOAuthTokenResult("new-access", null, expires), "me@x.com");
        Assert.Equal((CalendarIntegrationStatus.Connected, "me@x.com"), (r.Status, r.AccountDisplayName));
        Assert.Equal("refresh", _row!.RefreshToken); // kept when the provider sends none
        Assert.Null(_row.SelectedCalendarId);
        Assert.False(_row.SyncEnabled);
        Assert.Equal("cal9", Assert.Single(r.AvailableCalendars).ExternalCalendarId);
    }

    [Fact]
    public async Task CompleteConnect_keeps_a_selection_that_still_exists()
    {
        Connected(selected: "cal9");
        _google.Setup(g => g.ListCalendarsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarCalendarOption> { new("cal9", "Work", true) });
        await Sut().CompleteConnectAsync(_clientId, CalendarProvider.Google, new CalendarOAuthTokenResult("a", "r2", DateTimeOffset.UtcNow.AddHours(1)), null);
        Assert.Equal("cal9", _row!.SelectedCalendarId);
        Assert.Equal("r2", _row.RefreshToken);
    }

    [Fact]
    public async Task CompleteConnect_without_a_registered_client_fails()
    {
        var sut = new CalendarIntegrationService(_calendars.Object, _clients.Object, _uow.Object, _notifier.Object, _notifications.Object, [], _config.Object, _entitlements.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CompleteConnectAsync(_clientId, CalendarProvider.Google, new CalendarOAuthTokenResult("a", null, DateTimeOffset.UtcNow), null));
    }

    [Fact]
    public async Task FailConnect_records_the_error()
    {
        var r = await Sut().FailConnectAsync(_clientId, CalendarProvider.Google, "denied");
        Assert.Equal((CalendarIntegrationStatus.Error, "denied"), (r.Status, r.LastProblemMessage));
    }

    // ---------------------------------------------------------------- refresh / select / sync toggle / disconnect

    [Fact]
    public async Task RefreshCalendars_requires_a_connected_row()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google));
        Connected();
        _row!.Status = CalendarIntegrationStatus.Pending;
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google));
    }

    [Fact]
    public async Task RefreshCalendars_updates_the_list_and_marks_healthy()
    {
        Connected(selected: "cal1");
        _row!.IsHealthy = false;
        _google.Setup(g => g.ListCalendarsAsync("access", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarCalendarOption> { new("cal1", "Renamed", true), new("cal2", "Second", false) });
        await Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google);
        Assert.True(_row.IsHealthy);
        Assert.Equal(2, _row.Calendars.Count);
        Assert.Equal("cal1", _row.SelectedCalendarId);
    }

    [Fact]
    public async Task RefreshCalendars_provider_failure_flags_the_connection_and_wraps_the_error()
    {
        Connected();
        _google.Setup(g => g.ListCalendarsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("boom"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google));
        Assert.Contains("boom", TextKit.English(ex));
        Assert.False(_row!.IsHealthy);
        Assert.Equal("boom", _row.LastProblemMessage);
    }

    [Fact]
    public async Task RefreshCalendars_uses_a_refreshed_token_when_the_old_one_is_about_to_expire()
    {
        Connected();
        _row!.TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        _google.Setup(g => g.RefreshAccessTokenAsync("refresh", It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarOAuthTokenResult("fresh", null, DateTimeOffset.UtcNow.AddHours(1)));
        _google.Setup(g => g.ListCalendarsAsync("fresh", It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarCalendarOption>());
        await Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google);
        Assert.Equal("fresh", _row.AccessToken);
        Assert.Equal("refresh", _row.RefreshToken);
    }

    [Fact]
    public async Task SelectCalendar_validates_membership()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SelectCalendarAsync(_clientId, CalendarProvider.Google, "cal1"));
        Connected(selected: null);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SelectCalendarAsync(_clientId, CalendarProvider.Google, "nope"));
        var r = await Sut().SelectCalendarAsync(_clientId, CalendarProvider.Google, "cal1");
        Assert.Equal(("cal1", "Main"), (r.SelectedCalendarId, r.SelectedCalendarName));
    }

    [Fact]
    public async Task SetSyncEnabled_needs_a_calendar_before_turning_on()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, true));
        Connected(selected: null, sync: false);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, true));
        Assert.False((await Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, false)).SyncEnabled);
        _row!.SelectedCalendarId = "cal1";
        Assert.True((await Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, true)).SyncEnabled);
    }

    [Fact]
    public async Task Disconnect_revokes_tokens_and_wipes_the_row()
    {
        await Sut().DisconnectAsync(_clientId, CalendarProvider.Google); // nothing to do
        Connected();
        await Sut().DisconnectAsync(_clientId, CalendarProvider.Google);
        _google.Verify(g => g.RevokeAsync("access", "refresh", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(CalendarIntegrationStatus.Disconnected, _row!.Status);
        Assert.All(new[] { _row.AccessToken, _row.RefreshToken, _row.SelectedCalendarId, _row.AccountDisplayName }, Assert.Null);
        Assert.False(_row.SyncEnabled);
        _calendars.Verify(c => c.RemoveCalendars(_row), Times.Once);
    }


    // ---------------------------------------------------------------- sync callback

    private BookingCalendarSync SyncRow(Guid requestId, string op = "create") => new()
    {
        Id = Guid.NewGuid(), LastRequestId = requestId, LastOperation = op, Status = BookingCalendarSyncStatus.Pending
    };

    [Fact]
    public async Task Callback_success_marks_synced_and_stores_the_event_id()
    {
        var integ = Connected();
        var rid = Guid.NewGuid();
        var sync = SyncRow(rid);
        var apptId = Guid.NewGuid();
        _calendars.Setup(c => c.GetSyncAsync(apptId, integ.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sync);
        await Sut().ApplySyncCallbackAsync(new CalendarSyncCallbackRequest(_clientId, apptId, integ.Id, rid, true, "ev1", null));
        Assert.Equal((BookingCalendarSyncStatus.Synced, "ev1", null), (sync.Status, sync.ExternalEventId, sync.LastError));
        Assert.NotNull(integ.LastSyncedAt);
    }

    [Fact]
    public async Task Callback_success_after_cancel_marks_canceled()
    {
        var integ = Connected();
        var rid = Guid.NewGuid();
        var sync = SyncRow(rid, "cancel");
        var apptId = Guid.NewGuid();
        _calendars.Setup(c => c.GetSyncAsync(apptId, integ.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sync);
        await Sut().ApplySyncCallbackAsync(new CalendarSyncCallbackRequest(_clientId, apptId, integ.Id, rid, true, null, null));
        Assert.Equal(BookingCalendarSyncStatus.Canceled, sync.Status);
    }

    [Fact]
    public async Task Callback_failure_flags_the_integration_and_notifies_only_on_the_first_failure()
    {
        var integ = Connected();
        var rid = Guid.NewGuid();
        var sync = SyncRow(rid);
        var apptId = Guid.NewGuid();
        _calendars.Setup(c => c.GetSyncAsync(apptId, integ.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sync);
        var request = new CalendarSyncCallbackRequest(_clientId, apptId, integ.Id, rid, false, null, "quota");

        await Sut().ApplySyncCallbackAsync(request);
        Assert.Equal((BookingCalendarSyncStatus.Failed, "quota", false), (sync.Status, sync.LastError, integ.IsHealthy));
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.IntegrationUnhealthy, It.Is<NotificationText>(t => TextKit.Reads(t, "Google Calendar calendar sync needs attention", "quota")),
            null, null, null, null, "/settings/calendar-integrations", It.IsAny<CancellationToken>()), Times.Once);

        await Sut().ApplySyncCallbackAsync(request); // already unhealthy: no second notification
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Callback_for_unknown_or_superseded_requests_is_ignored()
    {
        var integ = Connected();
        var sync = SyncRow(Guid.NewGuid());
        var apptId = Guid.NewGuid();
        await Sut().ApplySyncCallbackAsync(new CalendarSyncCallbackRequest(_clientId, apptId, integ.Id, Guid.NewGuid(), true, null, null));
        _calendars.Setup(c => c.GetSyncAsync(apptId, integ.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sync);
        await Sut().ApplySyncCallbackAsync(new CalendarSyncCallbackRequest(_clientId, apptId, integ.Id, Guid.NewGuid(), true, null, null));
        Assert.Equal(BookingCalendarSyncStatus.Pending, sync.Status);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- bookings entitlement

    private void PlanWithoutBookings()
    {
        var plan = EntitlementKit.Plan((EntitlementKeys.Campaigns, "true"));
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>())).Returns(plan.Object.GetAsync(_clientId));
        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Bookings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.Bookings, EntitlementDeniedException.NotIncludedKey));
    }

    [Fact]
    public async Task Without_bookings_in_the_plan_calendar_sync_cant_be_set_up_but_can_be_turned_off()
    {
        Connected(selected: "cal1", sync: true);
        PlanWithoutBookings();

        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().RequestConnectAsync(_clientId, CalendarProvider.Google));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().RequestRefreshCalendarsAsync(_clientId, CalendarProvider.Google));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SelectCalendarAsync(_clientId, CalendarProvider.Google, "cal1"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, true));
        Assert.Equal(CalendarIntegrationStatus.Connected, _row!.Status); // nothing changed

        Assert.False((await Sut().SetSyncEnabledAsync(_clientId, CalendarProvider.Google, false)).SyncEnabled);
        _calendars.Setup(c => c.ListWithCalendarsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { _row! });
        Assert.Equal(CalendarProvider.All.Count, (await Sut().ListAsync(_clientId)).Count); // still listed
    }

    [Fact]
    public async Task Without_bookings_in_the_plan_nothing_is_pushed_to_external_calendars()
    {
        var integ = Connected();
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { integ });
        PlanWithoutBookings();

        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "created", default);

        _notifier.Verify(n => n.NotifySyncAsync(It.IsAny<CalendarSyncTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);
        _calendars.Verify(c => c.ListSyncTargetsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- trigger

    [Theory]
    [InlineData("created", "create")]
    [InlineData("rescheduled", "update")]
    [InlineData("canceled", "cancel")]
    public async Task Trigger_sends_the_mapped_operation_to_n8n_with_a_fresh_token(string change, string op)
    {
        var integ = Connected();
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { integ });
        BookingCalendarSync? added = null;
        _calendars.Setup(c => c.AddSync(It.IsAny<BookingCalendarSync>())).Callback<BookingCalendarSync>(s => added = s);
        CalendarSyncTriggerPayload? sent = null;
        _notifier.Setup(n => n.NotifySyncAsync(It.IsAny<CalendarSyncTriggerPayload>(), It.IsAny<CancellationToken>())).Callback<CalendarSyncTriggerPayload, CancellationToken>((p, _) => sent = p);

        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), change, default);

        Assert.Equal(op, sent!.Operation);
        Assert.Equal(("access", "cal1", "Asia/Beirut"), (sent.AccessToken, sent.ExternalCalendarId, sent.Timezone));
        Assert.Equal(added!.LastRequestId, sent.RequestId);
        Assert.Contains("Ann", sent.EventTitle);
        Assert.Equal("Status: booked\nLocation: Room 1", sent.EventDescription);
        Assert.Null(sent.ExternalEventId);
    }

    [Fact]
    public async Task Trigger_ignores_status_changes_and_clients_without_targets()
    {
        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "status_changed", default);
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration>());
        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "created", default);
        _notifier.Verify(n => n.NotifySyncAsync(It.IsAny<CalendarSyncTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Trigger_reuses_existing_sync_rows_and_never_touches_a_canceled_one()
    {
        var integ = Connected();
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { integ });
        var appt = Appt();
        var sync = new BookingCalendarSync { Id = Guid.NewGuid(), Status = BookingCalendarSyncStatus.Synced, ExternalEventId = "ev9" };
        _calendars.Setup(c => c.GetSyncAsync(appt.Id, integ.Id, It.IsAny<CancellationToken>())).ReturnsAsync(sync);
        var ops = new List<(string Op, string? Event)>();
        _notifier.Setup(n => n.NotifySyncAsync(It.IsAny<CalendarSyncTriggerPayload>(), It.IsAny<CancellationToken>()))
            .Callback<CalendarSyncTriggerPayload, CancellationToken>((p, _) => ops.Add((p.Operation, p.ExternalEventId)));

        await Sut().TriggerBookingSyncAsync(_clientId, appt, "rescheduled", default);
        await Sut().TriggerBookingSyncAsync(_clientId, appt, "created", default); // retried create becomes an update
        sync.Status = BookingCalendarSyncStatus.Canceled;
        await Sut().TriggerBookingSyncAsync(_clientId, appt, "rescheduled", default);

        Assert.Equal([("update", "ev9"), ("update", "ev9")], ops);
        _calendars.Verify(c => c.AddSync(It.IsAny<BookingCalendarSync>()), Times.Never);
    }

    [Fact]
    public async Task Trigger_skips_an_integration_whose_token_cannot_be_refreshed_and_never_throws()
    {
        var bad = Connected();
        bad.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(5);
        bad.RefreshToken = null;
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { bad });
        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "created", default);
        Assert.False(bad.IsHealthy);
        Assert.Contains("no refresh token", bad.LastProblemMessage);
        _notifier.Verify(n => n.NotifySyncAsync(It.IsAny<CalendarSyncTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);

        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "created", default); // swallowed by design
    }

    [Fact]
    public async Task Failed_token_refresh_marks_the_connection_unhealthy()
    {
        var integ = Connected();
        integ.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(5);
        _google.Setup(g => g.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("invalid_grant"));
        _calendars.Setup(c => c.ListSyncTargetsAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarIntegration> { integ });
        await Sut().TriggerBookingSyncAsync(_clientId, Appt(), "created", default);
        Assert.False(integ.IsHealthy);
        Assert.Contains("invalid_grant", integ.LastProblemMessage);
    }
}
