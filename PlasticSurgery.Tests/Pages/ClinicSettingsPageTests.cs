using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Tests.Controllers;
using static PlasticSurgery.Tests.Pages.PageKit;
using ClinicInfo = PlasticSurgery.Pages.Settings.ClinicInfoModel;
using IntegrationsPage = PlasticSurgery.Pages.Settings.IntegrationsModel;

namespace PlasticSurgery.Tests.Pages;

public class ClinicInfoPageTests
{
    private static readonly Clinic Glow = new()
    {
        Id = Guid.NewGuid(), Name = "Glow", Slug = "glow", Phone = "123", Email = "a@glow.test", Website = "https://glow.test",
        Address = "Main St", OperatingHours = "9-5", ConsultationInfo = "Free", Timezone = "UTC",
    };
    private readonly Mock<IClinicProfileService> _profile = new();
    private readonly Mock<IAvailabilityService> _availability = new();

    private ClinicInfo Sut(bool none = false) => new ClinicInfo(ClinicContext(none ? null : Glow).Object, _profile.Object, _availability.Object).Attach();

    private static AvailabilitySettingsDto Settings() => Blank<AvailabilitySettingsDto>() with { Timezone = "UTC", Days = [], Booking = Blank<BookingRulesDto>() };

    [Fact]
    public async Task The_general_tab_shows_the_clinic_details_and_the_availability_tab_loads_settings()
    {
        var model = Sut();
        Assert.IsType<PageResult>(await model.OnGetAsync(null, default));
        Assert.Equal(("Glow", "123", "a@glow.test", "general"), (model.Name, model.Phone, model.Email, model.ActiveTab));
        Assert.Null(model.Availability);

        _availability.Setup(a => a.GetSettingsAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var tab = Sut();
        await tab.OnGetAsync("AVAILABILITY", default);
        Assert.Equal("availability", tab.ActiveTab);
        Assert.NotNull(tab.Availability);

        var none = Sut(true);
        Assert.IsType<PageResult>(await none.OnGetAsync(null, default));
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public async Task Saving_details_redirects_and_a_validation_error_trims_the_name_and_redisplays()
    {
        var model = Sut(); model.Name = "Glow"; model.Phone = "999";
        Assert.IsType<RedirectToPageResult>(await model.OnPostAsync(default));
        Assert.Equal("Clinic details saved.", model.StatusMessage);
        _profile.Verify(p => p.UpdateDetailsAsync(Glow.Id, new UpdateClinicDetailsRequest("Glow", "999", null, null, null, null, null), It.IsAny<CancellationToken>()), Times.Once);

        _profile.Setup(p => p.UpdateDetailsAsync(Glow.Id, It.IsAny<UpdateClinicDetailsRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Name is required."));
        model.Name = "  ";
        Assert.IsType<PageResult>(await model.OnPostAsync(default));
        Assert.Equal(("Name is required.", ""), (model.ErrorMessage, model.Name));
        Assert.True(model.ClinicConfigured);

        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostAsync(default));
    }

    [Fact]
    public async Task Saving_the_schedule_converts_notice_hours_to_minutes()
    {
        var model = Sut();
        model.TimezoneId = "Asia/Beirut"; model.DefaultDurationMinutes = 45; model.BufferMinutes = 10; model.NoticeHours = 6; model.MaxAdvanceDays = 90;
        model.Days = [new() { DayOfWeek = 1, IsOpen = true, Start = "09:00", End = "17:00" }, new() { DayOfWeek = 0, IsOpen = false }];
        IReadOnlyList<DayRuleDto>? days = null; BookingRulesDto? booking = null;
        _availability.Setup(a => a.SaveScheduleAsync(Glow.Id, "Asia/Beirut", It.IsAny<IReadOnlyList<DayRuleDto>>(), It.IsAny<BookingRulesDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, IReadOnlyList<DayRuleDto>, BookingRulesDto, CancellationToken>((_, _, d, b, _) => { days = d; booking = b; });

        var result = await model.OnPostSaveAvailabilityAsync(default);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("availability", redirect.RouteValues!["tab"]);
        Assert.Equal(new BookingRulesDto(45, 10, 360, 90), booking);
        Assert.Equal(new DayRuleDto(0, false, "", ""), days![1]);        // closed days send empty times
        Assert.Equal("Opening hours saved.", model.StatusMessage);
    }

    [Fact]
    public async Task A_rejected_schedule_redisplays_the_attempted_values()
    {
        _availability.Setup(a => a.SaveScheduleAsync(Glow.Id, It.IsAny<string>(), It.IsAny<IReadOnlyList<DayRuleDto>>(), It.IsAny<BookingRulesDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Closing time must be after opening."));
        _availability.Setup(a => a.GetSettingsAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var model = Sut(); model.TimezoneId = "Asia/Dubai"; model.NoticeHours = 2;
        Assert.IsType<PageResult>(await model.OnPostSaveAvailabilityAsync(default));
        Assert.Equal(("availability", "Asia/Dubai", 120), (model.ActiveTab, model.Availability!.Timezone, model.Availability.Booking.MinimumNoticeMinutes));
        Assert.Equal("Glow", model.Name);                       // the general tab is refilled too
        Assert.Contains("Closing time", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostSaveAvailabilityAsync(default));
    }

    [Fact]
    public async Task Special_dates_are_parsed_saved_and_removed()
    {
        var model = Sut(); model.ExceptionDate = "2026-12-25"; model.ExceptionClosed = true; model.ExceptionReason = "Holiday";
        var redirect = Assert.IsType<RedirectToPageResult>(await model.OnPostAddExceptionAsync(default));
        Assert.Equal("availability", redirect.RouteValues!["tab"]);
        _availability.Verify(a => a.SaveExceptionAsync(Glow.Id, new DateOnly(2026, 12, 25), true, null, null, "Holiday", It.IsAny<CancellationToken>()), Times.Once);

        var id = Guid.NewGuid();
        await model.OnPostDeleteExceptionAsync(id, default);
        _availability.Verify(a => a.DeleteExceptionAsync(Glow.Id, id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Special date removed.", model.StatusMessage);

        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostAddExceptionAsync(default));
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostDeleteExceptionAsync(id, default));
    }

    [Fact]
    public async Task A_bad_special_date_redisplays_the_availability_tab()
    {
        _availability.Setup(a => a.GetSettingsAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
        var model = Sut(); model.ExceptionDate = "not a date";
        Assert.IsType<PageResult>(await model.OnPostAddExceptionAsync(default));
        Assert.Equal(("Pick a date.", "availability"), (model.ErrorMessage, model.ActiveTab));
        _availability.Verify(a => a.SaveExceptionAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        _availability.Setup(a => a.SaveExceptionAsync(Glow.Id, It.IsAny<DateOnly>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Times are required."));
        model.ExceptionDate = "2026-12-24";
        await model.OnPostAddExceptionAsync(default);
        Assert.Equal("Times are required.", model.ErrorMessage);
    }

    [Fact]
    public void Days_are_ordered_monday_first_and_an_unknown_timezone_is_kept_in_the_list()
    {
        var model = Sut();
        model.OnGetAsync("availability", default).GetAwaiter().GetResult();   // no settings yet: just exercising the helpers below
        var days = Enumerable.Range(0, 7).Select(d => new DayRuleDto(d, true, "09:00", "17:00")).ToList();
        _availability.Setup(a => a.GetSettingsAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Settings() with { Days = days });
        model.OnGetAsync("availability", default).GetAwaiter().GetResult();
        Assert.Equal([1, 2, 3, 4, 5, 6, 0], model.OrderedDays().Select(d => d.DayOfWeek));

        Assert.Equal(ClinicInfo.Timezones.Length, model.TimezoneOptions("UTC").Count());
        Assert.Equal("Mars/Base", model.TimezoneOptions("Mars/Base").First());
    }
}

public class IntegrationsPageTests
{
    private static readonly Clinic Glow = SomeClinic();
    private readonly Mock<IChannelIntegrationService> _integrations = new();
    private readonly Mock<ITelegramIntegrationService> _telegram = new();
    private readonly Mock<ITikTokIntegrationService> _tiktok = new();
    private readonly Mock<IInfobipWhatsAppIntegrationService> _infobip = new();

    private IntegrationsPage Sut(bool none = false, params (string Key, string? Value)[] config) =>
        new IntegrationsPage(ClinicContext(none ? null : Glow).Object, _integrations.Object, ControllerTestKit.Config(config), _telegram.Object, _tiktok.Object, _infobip.Object).Attach();

    [Fact]
    public async Task The_page_lists_channels_by_name_and_shows_the_webhook_url_only_for_the_alternate_provider()
    {
        _integrations.Setup(i => i.ListAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegrationResponse>
        {
            Blank<ChannelIntegrationResponse>() with { Channel = "telegram" }, Blank<ChannelIntegrationResponse>() with { Channel = "whatsapp" },
        });
        _tiktok.Setup(t => t.GetAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<TikTokIntegrationResponse>());
        _infobip.Setup(i => i.GetWebhookUrlAsync(Glow.Id, It.IsAny<CancellationToken>())).ReturnsAsync("https://hooks.example/x");

        var meta = Sut();
        Assert.IsType<PageResult>(await meta.OnGetAsync(default));
        Assert.Equal((true, Glow.Id, 2), (meta.ClinicConfigured, meta.ClinicId, meta.Channels.Count));
        Assert.False(meta.UsesInfobipWhatsApp);
        Assert.Null(meta.InfobipWebhookUrl);

        var alternate = Sut(false, ("WhatsApp:Provider", ChannelProvider.Infobip));
        await alternate.OnGetAsync(default);
        Assert.True(alternate.UsesInfobipWhatsApp);
        Assert.Equal("https://hooks.example/x", alternate.InfobipWebhookUrl);

        var none = Sut(true);
        await none.OnGetAsync(default);
        Assert.False(none.ClinicConfigured);
    }

    [Fact]
    public void Meta_login_settings_come_from_configuration_with_a_default_api_version()
    {
        var bare = Sut();
        Assert.Equal(("", "v21.0", "", ""), (bare.MetaAppId, bare.MetaGraphApiVersion, bare.MetaWhatsAppLoginConfigId, bare.MetaFacebookLoginConfigId));
        var set = Sut(false, ("Meta:AppId", "123"), ("Meta:GraphApiVersion", "v23.0"), ("Meta:WhatsAppLoginConfigId", "wa"), ("Meta:FacebookLoginConfigId", "fb"));
        Assert.Equal(("123", "v23.0", "wa", "fb"), (set.MetaAppId, set.MetaGraphApiVersion, set.MetaWhatsAppLoginConfigId, set.MetaFacebookLoginConfigId));
    }

    [Fact]
    public async Task Saving_a_channel_refuses_unknown_channels_and_telegram_which_has_its_own_flow()
    {
        var unknown = Sut(); unknown.Channel = "carrier-pigeon";
        Assert.IsType<BadRequestResult>(await unknown.OnPostSaveAsync(default));
        var telegram = Sut(); telegram.Channel = ChannelType.Telegram;
        Assert.IsType<BadRequestResult>(await telegram.OnPostSaveAsync(default));
        _integrations.Verify(i => i.SaveAsync(It.IsAny<SaveChannelIntegrationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Saving_a_channel_forwards_the_form_and_reports_plan_limits()
    {
        var model = Sut(); model.Channel = ChannelType.Facebook; model.DisplayName = "Page"; model.PageId = "p1"; model.AccessToken = "tok";
        Assert.IsType<RedirectToPageResult>(await model.OnPostSaveAsync(default));
        Assert.Equal("Facebook connection saved.", model.StatusMessage);
        _integrations.Verify(i => i.SaveAsync(It.Is<SaveChannelIntegrationRequest>(r => r.ClinicId == Glow.Id && r.Channel == ChannelType.Facebook && r.PageId == "p1"), It.IsAny<CancellationToken>()), Times.Once);

        _integrations.Setup(i => i.SaveAsync(It.IsAny<SaveChannelIntegrationRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Your plan allows 2 channels."));
        await model.OnPostSaveAsync(default);
        Assert.Equal("Your plan allows 2 channels.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostSaveAsync(default));
    }

    [Fact]
    public async Task Telegram_connect_reports_the_bot_or_the_failure()
    {
        _telegram.SetupSequence(t => t.ConnectAsync(Glow.Id, "123:abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ChannelIntegrationResponse>() with { DisplayName = "ClinicBot" })
            .ReturnsAsync(Blank<ChannelIntegrationResponse>() with { DisplayName = null })
            .ThrowsAsync(new TelegramApiException("Unauthorized", 401))
            .ThrowsAsync(new ArgumentException("Paste the bot token."));
        var model = Sut(); model.BotToken = "123:abc";
        await model.OnPostConnectTelegramAsync(default);
        Assert.StartsWith("Telegram connected — bot ClinicBot.", model.StatusMessage);
        await model.OnPostConnectTelegramAsync(default);
        Assert.Contains("(unnamed)", model.StatusMessage);
        await model.OnPostConnectTelegramAsync(default);
        Assert.Equal("Could not connect Telegram: Unauthorized", model.ErrorMessage);
        await model.OnPostConnectTelegramAsync(default);
        Assert.Equal("Could not connect Telegram: Paste the bot token.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostConnectTelegramAsync(default));
    }

    [Fact]
    public async Task Whatsapp_number_connect_reports_the_result_or_the_failure()
    {
        _infobip.SetupSequence(i => i.ConnectAsync(Glow.Id, "+961123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ChannelIntegrationResponse>() with { DisplayName = "+961 123" })
            .ThrowsAsync(new InvalidOperationException("Number not registered."));
        var model = Sut(); model.WhatsAppSenderNumber = "+961123";
        await model.OnPostConnectWhatsAppNumberAsync(default);
        Assert.Equal("WhatsApp connected — +961 123.", model.StatusMessage);
        await model.OnPostConnectWhatsAppNumberAsync(default);
        Assert.Equal("Could not connect WhatsApp: Number not registered.", model.ErrorMessage);
        Assert.IsType<RedirectToPageResult>(await Sut(true).OnPostConnectWhatsAppNumberAsync(default));
    }

    [Fact]
    public async Task Refresh_and_disconnect_actions_report_what_they_did()
    {
        _telegram.SetupSequence(t => t.RefreshStatusAsync(Glow.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<ChannelIntegrationResponse>() with { WebhookStatus = WebhookStatus.Active })
            .ReturnsAsync(Blank<ChannelIntegrationResponse>() with { WebhookStatus = "error" });
        var model = Sut();
        await model.OnPostRefreshTelegramAsync(default);
        Assert.Equal("Telegram webhook is active.", model.StatusMessage);
        await model.OnPostRefreshTelegramAsync(default);
        Assert.StartsWith("Telegram webhook check finished", model.StatusMessage);

        model.Channel = ChannelType.Instagram;
        await model.OnPostDisconnectAsync(default);
        Assert.Equal("Instagram disconnected.", model.StatusMessage);
        _integrations.Verify(i => i.DisconnectAsync(Glow.Id, ChannelType.Instagram, It.IsAny<CancellationToken>()), Times.Once);
        model.Channel = "other"; await model.OnPostDisconnectAsync(default);
        Assert.Equal("other disconnected.", model.StatusMessage);

        await model.OnPostDisconnectTikTokAsync(default);
        Assert.Equal("TikTok disconnected.", model.StatusMessage);

        var none = Sut(true);
        Assert.IsType<RedirectToPageResult>(await none.OnPostRefreshTelegramAsync(default));
        Assert.IsType<RedirectToPageResult>(await none.OnPostDisconnectAsync(default));
        Assert.IsType<RedirectToPageResult>(await none.OnPostDisconnectTikTokAsync(default));
    }
}
