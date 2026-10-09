using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Controllers.Admin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Controllers.Integrations;

namespace PlasticSurgery.Tests.Controllers;

public class CalendarOAuthControllerTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ICurrentClinicContext> _clinic = new();
    private readonly Mock<ICalendarIntegrationService> _calendar = new();
    private readonly Mock<ICalendarProviderClient> _google = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly IDataProtectionProvider _dp = new EphemeralDataProtectionProvider();
    private string? _baseUrl = "https://app.example.com";

    public CalendarOAuthControllerTests()
    {
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
        _config.SetupGet(c => c.IntegrationsOAuthStateLifetimeMinutes).Returns(10);
        _google.SetupGet(g => g.Provider).Returns(CalendarProvider.Google);
        _google.Setup(g => g.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>())).Returns((string r, string s) => $"https://accounts.google.com/auth?redirect={r}&state={Uri.EscapeDataString(s)}");
    }

    private CalendarOAuthController Sut(Action<HttpContext>? configure = null)
    {
        var c = new CalendarOAuthController(_calendar.Object, _clinic.Object, [_google.Object], ControllerTestKit.Config(("App:PublicBaseUrl", _baseUrl)), _dp, NullLogger<CalendarOAuthController>.Instance, _config.Object);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        c.TempData = new TempDataDictionary(c.HttpContext, Mock.Of<ITempDataProvider>());
        configure?.Invoke(c.HttpContext);
        return c;
    }

    private async Task<string> IssueStateAsync()
    {
        var redirect = Assert.IsType<RedirectResult>(await Sut().Connect(CalendarProvider.Google, default));
        return Uri.UnescapeDataString(redirect.Url.Split("state=")[1]);
    }

    [Fact]
    public async Task Connect_sends_the_user_to_the_provider_with_a_signed_state_and_marks_the_attempt_pending()
    {
        var result = Assert.IsType<RedirectResult>(await Sut().Connect(CalendarProvider.Google, default));
        Assert.StartsWith("https://accounts.google.com/auth", result.Url);
        Assert.Contains("redirect=https://app.example.com/calendar-oauth/google/callback", result.Url);
        _calendar.Verify(c => c.RequestConnectAsync(_clinicId, CalendarProvider.Google, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connect_rejects_unknown_providers_missing_clinics_and_unsafe_base_urls()
    {
        Assert.Equal("/settings/calendar-integrations", Assert.IsType<RedirectResult>(await Sut().Connect("yahoo", default)).Url);
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Assert.IsType<ForbidResult>(await Sut().Connect(CalendarProvider.Google, default));
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);

        _baseUrl = "http://insecure.example.com";
        var c = Sut();
        Assert.IsType<RedirectResult>(await c.Connect(CalendarProvider.Google, default));
        Assert.Contains("https://", c.TempData["ErrorMessage"]!.ToString());
        _baseUrl = null;
        var local = Sut(h => h.Request.Host = new HostString("localhost", 5000));
        await local.Connect(CalendarProvider.Google, default);
        Assert.Contains("public HTTPS address", local.TempData["ErrorMessage"]!.ToString());
        var publicHost = Sut(h => h.Request.Host = new HostString("clinic.onrender.com"));
        Assert.Contains("https://clinic.onrender.com/calendar-oauth/google/callback", Assert.IsType<RedirectResult>(await publicHost.Connect(CalendarProvider.Google, default)).Url);
    }

    [Fact]
    public async Task A_valid_callback_exchanges_the_code_and_completes_the_connection()
    {
        var state = await IssueStateAsync();
        var tokens = new CalendarOAuthTokenResult("a", "r", DateTimeOffset.UtcNow.AddHours(1));
        _google.Setup(g => g.ExchangeCodeAsync("code", "https://app.example.com/calendar-oauth/google/callback", It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        _google.Setup(g => g.GetAccountEmailAsync("a", It.IsAny<CancellationToken>())).ReturnsAsync("me@x.com");
        var controller = Sut();
        var result = Assert.IsType<RedirectResult>(await controller.Callback(CalendarProvider.Google, "code", state, null, null, default));
        Assert.Equal("/settings/calendar-integrations", result.Url);
        _calendar.Verify(c => c.CompleteConnectAsync(_clinicId, CalendarProvider.Google, tokens, "me@x.com", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Google Calendar connected.", controller.TempData["StatusMessage"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tampered-state")]
    public async Task Missing_or_tampered_state_fails_the_attempt_without_exchanging_the_code(string? state)
    {
        await Sut().Callback(CalendarProvider.Google, "code", state, null, null, default);
        _calendar.Verify(c => c.FailConnectAsync(_clinicId, CalendarProvider.Google, It.Is<string>(m => m.Contains("expired or was invalid")), It.IsAny<CancellationToken>()), Times.Once);
        _google.Verify(g => g.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task State_issued_to_another_clinic_or_provider_is_rejected()
    {
        var state = await IssueStateAsync();
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        await Sut().Callback(CalendarProvider.Google, "code", state, null, null, default);
        _google.Verify(g => g.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
        await Sut().Callback(CalendarProvider.Outlook, "code", state, null, null, default);
        _google.Verify(g => g.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Provider_errors_missing_codes_and_exchange_failures_are_recorded_and_shown()
    {
        var state = await IssueStateAsync();
        var denied = Sut();
        await denied.Callback(CalendarProvider.Google, null, state, "access_denied", "User said no", default);
        Assert.Equal("User said no", denied.TempData["ErrorMessage"]);
        _calendar.Verify(c => c.FailConnectAsync(_clinicId, CalendarProvider.Google, "User said no", It.IsAny<CancellationToken>()), Times.Once);

        var nocode = Sut();
        await nocode.Callback(CalendarProvider.Google, null, state, null, null, default);
        Assert.Contains("did not return", nocode.TempData["ErrorMessage"]!.ToString());

        _google.Setup(g => g.ExchangeCodeAsync("bad", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("invalid_grant"));
        var failed = Sut();
        await failed.Callback(CalendarProvider.Google, "bad", state, null, null, default);
        Assert.Contains("invalid_grant", failed.TempData["ErrorMessage"]!.ToString());
        _calendar.Verify(c => c.CompleteConnectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CalendarOAuthTokenResult>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Callback_for_unknown_providers_or_without_a_clinic()
    {
        Assert.IsType<RedirectResult>(await Sut().Callback("yahoo", "c", "s", null, null, default));
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Assert.IsType<ForbidResult>(await Sut().Callback(CalendarProvider.Google, "c", "s", null, null, default));
    }
}

public class TikTokOAuthControllerTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ICurrentClinicContext> _clinic = new();
    private readonly Mock<ITikTokIntegrationService> _tiktok = new();
    private readonly Mock<ITikTokProviderClient> _provider = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly IDataProtectionProvider _dp = new EphemeralDataProtectionProvider();

    public TikTokOAuthControllerTests()
    {
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
        _config.SetupGet(c => c.IntegrationsOAuthStateLifetimeMinutes).Returns(10);
        _provider.Setup(p => p.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>())).Returns((string r, string s) => $"https://tiktok.example/auth?state={Uri.EscapeDataString(s)}");
    }

    private TikTokOAuthController Sut()
    {
        var c = new TikTokOAuthController(_clinic.Object, _tiktok.Object, _provider.Object, ControllerTestKit.Config(("App:PublicBaseUrl", "https://app.example.com")), _dp, NullLogger<TikTokOAuthController>.Instance, _config.Object);
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        c.TempData = new TempDataDictionary(c.HttpContext, Mock.Of<ITempDataProvider>());
        return c;
    }

    [Fact]
    public async Task Full_connect_round_trip_and_rejections()
    {
        var redirect = Assert.IsType<RedirectResult>(await Sut().Connect(default));
        var state = Uri.UnescapeDataString(redirect.Url.Split("state=")[1]);

        var tokens = new TikTokOAuthTokenResult("a", "r", DateTimeOffset.UtcNow.AddHours(1), null);
        var account = new TikTokAccountInfo("o", null, "Clinic", null);
        _provider.Setup(p => p.ExchangeCodeAsync("code", "https://app.example.com/tiktok-oauth/callback", It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        _provider.Setup(p => p.GetAccountInfoAsync("a", It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var ok = Sut();
        Assert.Equal("/settings/integrations", Assert.IsType<RedirectResult>(await ok.Callback("code", state, null, null, default)).Url);
        _tiktok.Verify(t => t.CompleteConnectAsync(_clinicId, tokens, account, It.IsAny<CancellationToken>()), Times.Once);

        await Sut().Callback("code", "garbage", null, null, default);
        await Sut().Callback("code", null, null, null, default);
        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        await Sut().Callback("code", state, null, null, default);
        _provider.Verify(p => p.ExchangeCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clinicId);
        await Sut().Callback(null, state, "access_denied", "nope", default);
        await Sut().Callback(null, state, null, null, default);
        _provider.Setup(p => p.ExchangeCodeAsync("boom", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("x"));
        await Sut().Callback("boom", state, null, null, default);
        _tiktok.Verify(t => t.FailConnectAsync(_clinicId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(4));

        _clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Assert.IsType<ForbidResult>(await Sut().Connect(default));
        Assert.IsType<ForbidResult>(await Sut().Callback("c", "s", null, null, default));
    }
}

public class IngestAndAdminControllerTests
{
    [Fact]
    public async Task Benchmark_generation_callback_maps_parser_and_service_outcomes()
    {
        var benchmark = new Mock<IKnowledgeBenchmarkService>();
        var controller = new KnowledgeBenchmarkIngestController(benchmark.Object).With();
        var id = Guid.NewGuid();
        Assert.IsType<BadRequestObjectResult>((await controller.ReceiveQuestions(id, ControllerTestKit.Json("\"not an object\""), default)).Result);

        var body = ControllerTestKit.Json("{\"questions\":[]}");
        benchmark.Setup(b => b.ReceiveGenerationResultAsync(id, It.IsAny<GeneratorResponse>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationReceiveResult(GenerationReceiveStatus.NotFound, null, "no such generation"));
        Assert.IsType<NotFoundObjectResult>((await controller.ReceiveQuestions(id, body, default)).Result);
        benchmark.Setup(b => b.ReceiveGenerationResultAsync(id, It.IsAny<GeneratorResponse>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationReceiveResult(GenerationReceiveStatus.NotAccepting, null, "closed"));
        Assert.IsType<ConflictObjectResult>((await controller.ReceiveQuestions(id, body, default)).Result);
        benchmark.Setup(b => b.ReceiveGenerationResultAsync(id, It.IsAny<GeneratorResponse>(), It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationReceiveResult(GenerationReceiveStatus.AlreadyProcessed, null, null));
        var ok = (BenchmarkGenerationCallbackResponse)Assert.IsType<OkObjectResult>((await controller.ReceiveQuestions(id, body, default)).Result).Value!;
        Assert.True(ok.AlreadyProcessed);
    }

    private static async Task<IActionResult> Run(Func<Task<IActionResult>> action) => await action();

    [Fact]
    public async Task Platform_admin_controllers_pass_requests_through_and_map_missing_rows_to_404()
    {
        var id = Guid.NewGuid();
        var clinics = new Mock<IClinicAdminService>();
        var c = new ClinicsAdminController(clinics.Object).With();
        Assert.IsType<NotFoundResult>(await c.Get(id, default));
        clinics.Setup(s => s.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new ClinicDetail(id, "n", "s", null, null, null, null, "UTC", null, null, null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.IsType<OkObjectResult>(await c.Get(id, default));
        await c.List("q", true, 1, default); await c.Options(default); await c.Counts(id, default);
        await c.SetActive(id, new ActiveBody(false), default);
        clinics.Verify(s => s.SetActiveAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);

        var settings = new Mock<ISettingsService>();
        var sc = new SettingsAdminController(settings.Object).With(h => h.Request.Headers[RequirePlatformAdminKeyAttribute.ActorHeaderName] = "ops");
        await sc.List(default);
        await sc.Set("S", "K", new SetSettingRequest("5", "note"), default);
        settings.Verify(s => s.SetAsync("S", "K", "5", "note", "ops", It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<NotFoundResult>(await sc.Reset("S", "K", default));
        settings.Setup(s => s.ResetAsync("S", "K", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await sc.Reset("S", "K", default));

        var cache = new Mock<ICacheAdminService>();
        var cc = new CacheAdminController(cache.Object).With();
        await cc.List("p", default); await cc.Get("k", default); await cc.RemoveByPrefix("p:", default); await cc.Clear(default);
        Assert.IsType<NoContentResult>(await cc.Remove("k", default));

        var automation = new Mock<IAutomationCleanupService>();
        var ac = new AutomationAdminController(automation.Object, NullLogger<AutomationAdminController>.Instance).With();
        automation.Setup(a => a.DeleteClinicAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(AutomationDeleteOutcome.NotFound);
        Assert.IsType<NotFoundResult>(await ac.DeleteClinic(id, default));
        automation.Setup(a => a.DeleteClinicAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(AutomationDeleteOutcome.NotAutomationClinic);
        Assert.IsType<ConflictObjectResult>(await ac.DeleteClinic(id, default));
        automation.Setup(a => a.DeleteClinicAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(AutomationDeleteOutcome.Deleted);
        Assert.IsType<NoContentResult>(await ac.DeleteClinic(id, default));
        await ac.ListClinics(default);

        var staff = new Mock<IStaffAdminService>();
        var st = new StaffAdminController(staff.Object).With();
        Assert.IsType<NotFoundResult>(await st.Get("u1", default));
        await st.List(null, null, 1, default); await st.SetMembershipActive("u1", new ActiveBody(true), default); await st.SetLocked("u1", new FlagBody(true), default);
        await st.SetPassword("u1", new PasswordBody("pw"), default); await st.SetEmailConfirmed("u1", new FlagBody(true), default);
        staff.Verify(s => s.SetPasswordAsync("u1", "pw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lead_content_channel_and_overview_admin_controllers_delegate()
    {
        var id = Guid.NewGuid();
        var leads = new Mock<ILeadAdminService>();
        var lc = new LeadsAdminController(leads.Object).With();
        Assert.IsType<NotFoundResult>(await lc.Get(id, default));
        Assert.IsType<NotFoundResult>(await lc.Conversation(id, default));
        await lc.List(null, null, null, 1, default); await lc.Events(id, default); await lc.Conversations(null, null, null, null, null, 1, default);
        await lc.ConversationMessages(id, 0, default); await lc.ConversationMessages(id, 25, default);
        leads.Verify(l => l.MessagesAsync(id, 500, It.IsAny<CancellationToken>()), Times.Once);
        leads.Verify(l => l.MessagesAsync(id, 25, It.IsAny<CancellationToken>()), Times.Once);
        await lc.SetMode(id, new ModeBody("human"), default); await lc.SetStatus(id, new StatusBody("closed"), default); await lc.SetAppointmentStatus(id, new StatusBody("attended"), default);
        await lc.Messages(null, true, null, null, 1, default); await lc.Appointments(null, null, null, false, 1, default);
        leads.Verify(l => l.SetModeAsync(id, "human", It.IsAny<CancellationToken>()), Times.Once);

        var content = new Mock<IContentAdminService>();
        var cc = new ContentAdminController(content.Object).With();
        Assert.IsType<NotFoundResult>(await cc.Campaign(id, default));
        Assert.IsType<NotFoundResult>(await cc.Document(id, default));
        Assert.IsType<NotFoundResult>(await cc.SearchSettings(id, default));
        await cc.Campaigns(null, null, 1, default); await cc.Recipients(id, null, 1, default); await cc.CancelCampaign(id, default); await cc.Templates(null, null, default);
        await cc.Procedures(null, default); await cc.SetProcedureActive(id, new ActiveBody(true), default); await cc.Documents(null, null, null, 1, default);
        await cc.SetDocumentActive(id, new ActiveBody(true), default); await cc.Websites(null, default); await cc.UpdateSearchSettings(id, new SearchSettingsBody(4, 0.4), default);
        content.Verify(s => s.UpdateSearchSettingsAsync(id, 4, 0.4, It.IsAny<CancellationToken>()), Times.Once);

        var channels = new Mock<IChannelAdminService>();
        var ch = new ChannelsAdminController(channels.Object).With();
        Assert.IsType<NotFoundResult>(await ch.Get(id, default));
        await ch.List(null, null, true, default); await ch.HealthEvents(id, default); await ch.Disconnect(id, default); await ch.ConnectInfobip(id, new InfobipSenderBody("+44"), default);
        await ch.Calendars(null, default); await ch.DisconnectCalendar(id, default); await ch.SetCalendarSync(id, new FlagBody(true), default); await ch.TikTok(null, default); await ch.DisconnectTikTok(id, default);
        channels.Verify(s => s.ConnectInfobipSenderAsync(id, "+44", It.IsAny<CancellationToken>()), Times.Once);

        var overview = new Mock<IOverviewAdminService>();
        var ov = new OverviewAdminController(overview.Object).With();
        await ov.Totals(default); await ov.DailyMessages(0, default); await ov.DailyMessages(30, default); await ov.Problems(default);
        await ov.RecentClinics(0, default); await ov.RecentClinics(5, default); await ov.Events(null, null, 1, default); await ov.EventTypes(default);
        overview.Verify(s => s.DailyMessagesAsync(14, It.IsAny<CancellationToken>()), Times.Once);
        overview.Verify(s => s.RecentClinicsAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
