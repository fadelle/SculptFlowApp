using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Client;
using PlasticSurgery.Controllers.Integrations;

namespace PlasticSurgery.Tests.Controllers;

public class ChannelIntegrationsControllerTests : ClientControllerTestBase
{
    private readonly Mock<IChannelIntegrationService> _integrations = new();
    private readonly Mock<ITelegramIntegrationService> _telegram = new();

    private ChannelIntegrationsController Sut() =>
        new ChannelIntegrationsController(_integrations.Object, _telegram.Object, Clinic.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task List_and_refresh_telegram_use_the_signed_in_clinic()
    {
        _integrations.Setup(i => i.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegrationResponse>());
        _telegram.Setup(t => t.RefreshStatusAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ChannelIntegrationResponse>());
        Assert.IsType<OkObjectResult>((await Sut().List(default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().RefreshTelegram(default)).Result);
    }

    [Fact]
    public async Task Save_overrides_the_clinic_id_in_the_body()
    {
        SaveChannelIntegrationRequest? seen = null;
        _integrations.Setup(i => i.SaveAsync(It.IsAny<SaveChannelIntegrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SaveChannelIntegrationRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        await Sut().Save(Blank<SaveChannelIntegrationRequest>() with { ClinicId = Guid.NewGuid() }, default);
        Assert.Equal(ClinicId, seen!.ClinicId);
    }

    [Fact]
    public async Task Disconnect_is_scoped_to_the_clinic()
    {
        Assert.IsType<NoContentResult>(await Sut().Disconnect("telegram", default));
        _integrations.Verify(i => i.DisconnectAsync(ClinicId, "telegram", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Telegram_connect_succeeds_with_the_clinic_and_token()
    {
        _telegram.Setup(t => t.ConnectAsync(ClinicId, "123:abc", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ChannelIntegrationResponse>());
        Assert.IsType<OkObjectResult>((await Sut().ConnectTelegram(new ConnectTelegramRequest("123:abc"), default)).Result);
    }

    [Theory]
    [InlineData("argument", 400)]
    [InlineData("telegram", 400)]
    [InlineData("invalid", 409)]
    public async Task Telegram_connect_maps_errors(string kind, int status)
    {
        Exception ex = kind switch
        {
            "argument" => new ArgumentException("bad token"),
            "telegram" => new TelegramApiException("Unauthorized", 401),
            _ => new InvalidOperationException("already connected elsewhere"),
        };
        _telegram.Setup(t => t.ConnectAsync(ClinicId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        var result = (await Sut().ConnectTelegram(new ConnectTelegramRequest("x"), default)).Result;
        Assert.Equal(status, ((ObjectResult)result!).StatusCode);
    }

    [Fact]
    public async Task WhatsApp_connect_uses_the_signed_in_clinic_and_maps_provider_failures_to_502()
    {
        ConnectWhatsAppRequest? seen = null;
        _integrations.Setup(i => i.ConnectWhatsAppAsync(It.IsAny<ConnectWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectWhatsAppRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        var request = Blank<ConnectWhatsAppRequest>() with { ClinicId = Guid.NewGuid() };
        Assert.IsType<OkObjectResult>((await Sut().ConnectWhatsApp(request, default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _integrations.Setup(i => i.ConnectWhatsAppAsync(It.IsAny<ConnectWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaGraphApiException("meta says no"));
        var failed = Assert.IsType<ObjectResult>((await Sut().ConnectWhatsApp(request, default)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
        Assert.Equal("meta says no", ((ProblemDetails)failed.Value!).Detail);
    }

    [Fact]
    public async Task Facebook_connect_uses_the_signed_in_clinic_and_maps_failures_to_502()
    {
        ConnectFacebookRequest? seen = null;
        _integrations.Setup(i => i.ConnectFacebookAsync(It.IsAny<ConnectFacebookRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectFacebookRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        var request = new ConnectFacebookRequest(Guid.NewGuid(), "code", null);
        Assert.IsType<OkObjectResult>((await Sut().ConnectFacebook(request, default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _integrations.Setup(i => i.ConnectFacebookAsync(It.IsAny<ConnectFacebookRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no page"));
        var failed = Assert.IsType<ObjectResult>((await Sut().ConnectFacebook(request, default)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
    }

}

/// <summary>The WhatsApp token inspector lives on the platform-admin side (it uses SculptFlow's own Meta app secret).</summary>
public class WhatsAppTokenDebugTests
{
    private readonly Mock<IMetaGraphClient> _graph = new();

    private ChannelAdminService Sut() => new(Mock.Of<IChannelAdminRepository>(), Mock.Of<IChannelIntegrationService>(),
        Mock.Of<ICalendarIntegrationService>(), Mock.Of<ITikTokIntegrationService>(), _graph.Object);

    [Fact]
    public async Task An_access_token_is_inspected_without_exchanging_a_code()
    {
        _graph.Setup(g => g.GetGrantedScopesAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "whatsapp_business_management" });
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo>());
        var result = await Sut().DebugWhatsAppTokenAsync(new DebugTokenRequest(AccessToken: "tok"), default);
        Assert.Equal(["whatsapp_business_management"], result.Scopes);
        _graph.Verify(g => g.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_code_is_exchanged_first()
    {
        _graph.Setup(g => g.ExchangeCodeForTokenAsync("c", "https://r", It.IsAny<CancellationToken>())).ReturnsAsync("exchanged");
        _graph.Setup(g => g.GetGrantedScopesAsync("exchanged", It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("exchanged", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo>());
        await Sut().DebugWhatsAppTokenAsync(new DebugTokenRequest(Code: "c", RedirectUri: "https://r"), default);
        _graph.Verify(g => g.GetGrantedScopesAsync("exchanged", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Missing_input_is_a_bad_request_and_provider_failures_propagate_for_the_502_mapping()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().DebugWhatsAppTokenAsync(new DebugTokenRequest(), default));
        _graph.Setup(g => g.GetGrantedScopesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new MetaGraphApiException("expired"));
        await Assert.ThrowsAsync<MetaGraphApiException>(() => Sut().DebugWhatsAppTokenAsync(new DebugTokenRequest(AccessToken: "t"), default));
        Assert.Equal(502, PlasticSurgery.Controllers.Filters.ApiErrorsAttribute.StatusFor(new MetaGraphApiException("x")));
    }

    [Fact]
    public async Task The_admin_endpoint_returns_the_inspection()
    {
        var service = new Mock<IChannelAdminService>();
        service.Setup(s => s.DebugWhatsAppTokenAsync(It.IsAny<DebugTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppTokenDebugResult(["a"], []));
        var result = await new PlasticSurgery.Controllers.Admin.ChannelsAdminController(service.Object).DebugWhatsAppToken(new DebugTokenRequest(AccessToken: "t"), default);
        Assert.IsType<OkObjectResult>(result);
    }
}

public class WebsiteSourcesControllerTests : ClientControllerTestBase
{
    private readonly Mock<IWebsiteSourceService> _websites = new();
    private WebsiteSourcesController Sut() => new WebsiteSourcesController(_websites.Object, Clinic.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task List_create_and_set_active_are_scoped_to_the_clinic()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsiteSourceResponse>());
        _websites.Setup(w => w.CreateAsync(ClinicId, It.IsAny<CreateWebsiteSourceRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        _websites.Setup(w => w.SetActiveAsync(ClinicId, id, false, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.List(default)).Result);
        Assert.IsType<AcceptedAtActionResult>((await sut.Create(Blank<CreateWebsiteSourceRequest>(), default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SetActive(id, new SetKnowledgeActiveRequest(false), default)).Result);
    }

    [Fact]
    public async Task Get_pages_set_active_and_rescrape_are_404_for_an_unknown_source()
    {
        var id = Guid.NewGuid();
        var sut = Sut();
        Assert.IsType<NotFoundResult>((await sut.GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.Pages(id, null, 0, 200, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.Rescrape(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.SetActive(id, new SetKnowledgeActiveRequest(true), default)).Result);
        Assert.IsType<NotFoundResult>(await sut.Delete(id, default));
    }

    [Fact]
    public async Task Pages_are_listed_only_after_the_source_is_confirmed_to_belong_to_the_clinic()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.GetAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceDetailResponse>());
        _websites.Setup(w => w.ListPagesAsync(ClinicId, id, "failed", 1, 2, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsitePageResponse>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.GetById(id, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Pages(id, "failed", 1, 2, default)).Result);
    }

    [Fact]
    public async Task Rescrape_is_202_and_delete_is_204_when_found()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.RescrapeAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        _websites.Setup(w => w.DeleteAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<AcceptedResult>((await Sut().Rescrape(id, default)).Result);
        Assert.IsType<NoContentResult>(await Sut().Delete(id, default));
    }

    [Fact]
    public async Task Rescrape_and_delete_turn_argument_errors_into_409()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.RescrapeAsync(ClinicId, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("already running"));
        _websites.Setup(w => w.DeleteAsync(ClinicId, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("in use"));
        Assert.IsType<ConflictObjectResult>((await Sut().Rescrape(id, default)).Result);
        Assert.IsType<ConflictObjectResult>(await Sut().Delete(id, default));
    }
}

public class SmallIntegrationControllerTests
{
    private const string TikTokSecret = "tt-secret";
    private static readonly DateTimeOffset TikTokNow = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static TikTokController TikTok(string body, string? header, string? secret = TikTokSecret, DateTimeOffset? now = null) =>
        new TikTokController(Mock.Of<Microsoft.Extensions.Logging.ILogger<TikTokController>>(),
                ControllerTestKit.Config(("TikTok:ClientSecret", secret)), new FakeTime(now ?? TikTokNow))
            .With(ctx =>
            {
                ctx.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
                if (header is not null) ctx.Request.Headers["TikTok-Signature"] = header;
            });

    private static string TikTokSignature(string body, long seconds, string secret = TikTokSecret) =>
        $"t={seconds},s=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes($"{seconds}.{body}"))).ToLowerInvariant();

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task TikTok_reachability_check_answers_ok_and_a_signed_delivery_is_accepted()
    {
        Assert.IsType<OkObjectResult>(TikTok("{}", null).Reachable());
        const string body = "{\"event\":\"x\"}";
        var header = TikTokSignature(body, TikTokNow.ToUnixTimeSeconds());
        Assert.IsType<OkResult>(await TikTok(body, header).Receive(default));
        Assert.IsType<OkResult>(await TikTok(body, header.ToUpperInvariant().Replace("T=", "t=").Replace(",S=", ",s=")).Receive(default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("t=abc,s=00")]
    [InlineData("t=1760356800,s=deadbeef")]
    public async Task TikTok_deliveries_without_a_valid_signature_are_rejected(string? header)
    {
        Assert.IsType<UnauthorizedResult>(await TikTok("{}", header).Receive(default));
    }

    [Fact]
    public async Task TikTok_rejects_old_timestamps_a_body_that_does_not_match_and_a_missing_secret()
    {
        const string body = "{\"a\":1}";
        var oldSeconds = TikTokNow.AddMinutes(-10).ToUnixTimeSeconds();
        Assert.IsType<UnauthorizedResult>(await TikTok(body, TikTokSignature(body, oldSeconds)).Receive(default));
        Assert.IsType<UnauthorizedResult>(await TikTok("{\"a\":2}", TikTokSignature(body, TikTokNow.ToUnixTimeSeconds())).Receive(default));
        Assert.IsType<UnauthorizedResult>(await TikTok(body, TikTokSignature(body, TikTokNow.ToUnixTimeSeconds()), secret: null).Receive(default));
    }

    [Fact]
    public async Task A_signed_body_that_is_not_json_is_a_bad_request()
    {
        const string body = "not json";
        Assert.IsType<BadRequestResult>(await TikTok(body, TikTokSignature(body, TikTokNow.ToUnixTimeSeconds())).Receive(default));
    }

    [Fact]
    public async Task Calendar_sync_callback_passes_the_payload_to_the_service()
    {
        var service = new Mock<ICalendarIntegrationService>();
        var request = (CalendarSyncCallbackRequest)RuntimeHelpers.GetUninitializedObject(typeof(CalendarSyncCallbackRequest));
        var result = await new CalendarIntegrationsIngestController(service.Object).SyncCallback(request, default);
        Assert.IsType<OkResult>(result);
        service.Verify(s => s.ApplySyncCallbackAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }
}
