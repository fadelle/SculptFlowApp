using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Engines.WebScraping;
using PlasticSurgery.Business.HttpClients.WebScraping;
using PlasticSurgery.Business.Providers.Channels;
using PlasticSurgery.Business.Providers.WhatsApp;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Tests.Support;
using Microsoft.Extensions.Hosting;

namespace PlasticSurgery.Tests.Providers;

public class MetaWhatsAppProviderTests
{
    private readonly FakeHttpHandler _http = new();
    private MetaWhatsAppProvider Sut(string? version = null) => new(_http.CreateClient(), new ConfigurationBuilder()
        .AddInMemoryCollection(version is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["Meta:GraphApiVersion"] = version }).Build());

    private static ChannelIntegration Integration() => new() { PhoneNumberId = "P1", AccessToken = "tok", Channel = ChannelType.WhatsApp };

    [Fact]
    public void Readiness_needs_phone_number_and_token()
    {
        Assert.True(Sut().IsReady(Integration()));
        Assert.False(Sut().IsReady(new ChannelIntegration { PhoneNumberId = "P1" }));
        Assert.False(Sut().IsReady(new ChannelIntegration { AccessToken = "t" }));
        Assert.Equal(ChannelProvider.Meta, Sut().Name);
    }

    [Fact]
    public async Task Text_goes_to_the_phone_numbers_messages_endpoint_with_a_bearer_token()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"wamid.1\"}]}");
        Assert.Equal("wamid.1", await Sut("v22.0").SendTextAsync(Integration(), "+96170123456", "hello"));
        var req = _http.Requests[0];
        Assert.Equal("https://graph.facebook.com/v22.0/P1/messages", req.Uri.ToString());
        Assert.Equal("Bearer tok", req.Headers["Authorization"]);
        Assert.Contains("\"body\":\"hello\"", req.Body);
    }

    [Fact]
    public async Task Template_with_and_without_body_parameters()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"a\"}]}").Enqueue(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"b\"}]}");
        await Sut().SendTemplateAsync(Integration(), "961", new WhatsAppTemplateSend("promo", "en", []));
        Assert.DoesNotContain("components", _http.Requests[0].Body);
        await Sut().SendTemplateAsync(Integration(), "961", new WhatsAppTemplateSend("promo", "en", ["Ann", "20%"]));
        Assert.Contains("\"components\"", _http.Requests[1].Body);
        Assert.Contains("\"text\":\"Ann\"", _http.Requests[1].Body);
    }

    [Fact]
    public async Task Failures_become_send_exceptions()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad number\"}}");
        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextAsync(Integration(), "x", "hi"));
        Assert.Contains("bad number", ex.Message);
        _http.Enqueue(HttpStatusCode.OK, "{\"messages\":[{\"id\":null}]}");
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextAsync(Integration(), "x", "hi"));
    }
}

public class InfobipWhatsAppProviderTests
{
    private readonly Mock<IInfobipClient> _client = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://app.example.com" }).Build();

    private InfobipWhatsAppProvider Sut() => new(_client.Object, _configuration, NullLogger<InfobipWhatsAppProvider>.Instance);

    private static ChannelIntegration Integration() => new() { Id = Guid.NewGuid(), ProviderSenderId = "447000", WebhookVerifyToken = "tok", Provider = ChannelProvider.Infobip };

    [Fact]
    public async Task Text_is_sent_from_the_sender_to_a_digits_only_number_with_a_notify_url()
    {
        InfobipWhatsAppTextMessage? sent = null;
        _client.Setup(c => c.SendWhatsAppTextAsync(It.IsAny<InfobipWhatsAppTextMessage>(), It.IsAny<CancellationToken>()))
            .Callback<InfobipWhatsAppTextMessage, CancellationToken>((m, _) => sent = m).ReturnsAsync(new InfobipSendResult("srv-1", "PENDING", null));
        var integration = Integration();
        Assert.Equal("srv-1", await Sut().SendTextAsync(integration, "+961 70 123 456", "hi"));
        Assert.Equal(("447000", "96170123456", "hi"), (sent!.From, sent.To, sent.Content.Text));
        Assert.Contains($"/connections/{integration.Id}/events?token=tok", sent.NotifyUrl);
        Assert.True(Guid.TryParse(sent.MessageId, out _));
        Assert.True(Sut().IsReady(integration));
        Assert.False(Sut().IsReady(new ChannelIntegration()));
    }

    [Fact]
    public async Task Template_parameters_are_positional()
    {
        InfobipWhatsAppTemplateMessage? sent = null;
        _client.Setup(c => c.SendWhatsAppTemplateAsync(It.IsAny<InfobipWhatsAppTemplateMessage>(), It.IsAny<CancellationToken>()))
            .Callback<InfobipWhatsAppTemplateMessage, CancellationToken>((m, _) => sent = m).ReturnsAsync(new InfobipSendResult("t-1", null, null));
        await Sut().SendTemplateAsync(Integration(), "96170", new WhatsAppTemplateSend("promo", "en", ["Ann"]));
        Assert.Equal(("promo", "en", "Ann"), (sent!.Content.TemplateName, sent.Content.Language, sent.Content.TemplateData.Body.Placeholders[0]));
    }

    [Theory]
    [InlineData(true, false, "in time")]
    [InlineData(false, true, "didn't accept")]
    [InlineData(false, false, "right now")]
    public async Task Provider_errors_become_neutral_messages_that_never_mention_infobip(bool timeout, bool rejected, string expected)
    {
        var ex = new InfobipApiException("Infobip internal detail", rejected ? 400 : 503, null) { IsTimeout = timeout };
        _client.Setup(c => c.SendWhatsAppTextAsync(It.IsAny<InfobipWhatsAppTextMessage>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        var thrown = await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextAsync(Integration(), "961", "hi"));
        Assert.Contains(expected, thrown.Message);
        Assert.DoesNotContain("nfobip", thrown.Message);
    }
}

public class WhatsAppTemplateProviderTests
{
    private readonly Mock<IMetaGraphClient> _meta = new();
    private readonly Mock<IInfobipClient> _infobip = new();

    private static WhatsAppTemplate Template(string body = "Hi {{1}}, offer {{2}}", string? header = null, string? headerContent = null, string? footer = null, string? buttons = null, string? variables = null) => new()
    {
        Id = Guid.NewGuid(), Name = "promo", Category = "marketing", Language = "en_US", Body = body, HeaderType = header, HeaderContent = headerContent, Footer = footer, ButtonsJson = buttons, VariablesJson = variables, MetaTemplateId = "tpl-1"
    };

    [Fact]
    public async Task Meta_provider_builds_components_and_wraps_graph_errors()
    {
        object? components = null;
        _meta.Setup(m => m.CreateMessageTemplateAsync("W1", "tok", "promo", "marketing", "en_US", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, object, CancellationToken>((_, _, _, _, _, c, _) => components = c)
            .ReturnsAsync(new MetaTemplateCreateResult("id1", "PENDING"));
        var provider = new MetaWhatsAppTemplateProvider(_meta.Object);
        var integration = new ChannelIntegration { WhatsAppBusinessId = "W1", AccessToken = "tok" };
        Assert.True(provider.IsReady(integration));
        Assert.False(provider.IsReady(new ChannelIntegration()));

        var r = await provider.SubmitAsync(integration, Template(header: "text", headerContent: "Hello", footer: "Bye", buttons: "[{\"type\":\"QUICK_REPLY\",\"text\":\"Yes\"}]"));
        Assert.Equal(("id1", "PENDING"), (r.ProviderTemplateId, r.Status));
        var json = JsonSerializer.Serialize(components);
        Assert.Contains("\"type\":\"HEADER\"", json);
        Assert.Contains("\"format\":\"TEXT\"", json);
        Assert.Contains("\"type\":\"FOOTER\"", json);
        Assert.Contains("\"type\":\"BUTTONS\"", json);

        await provider.SubmitAsync(integration, Template(header: "image"));
        Assert.Contains("\"format\":\"IMAGE\"", JsonSerializer.Serialize(components));
        await provider.SubmitAsync(integration, Template(header: "none"));
        Assert.DoesNotContain("HEADER", JsonSerializer.Serialize(components));

        _meta.Setup(m => m.CreateMessageTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaGraphApiException("invalid"));
        await Assert.ThrowsAsync<WhatsAppTemplateProviderException>(() => provider.SubmitAsync(integration, Template()));

        _meta.Setup(m => m.GetMessageTemplateStatusAsync("tpl-1", "tok", It.IsAny<CancellationToken>())).ReturnsAsync(new MetaTemplateStatusResult("REJECTED", "why"));
        var status = await provider.GetStatusAsync(integration, Template());
        Assert.Equal(("REJECTED", "why"), (status.Status, status.RejectedReason));
    }

    private static string Request(WhatsAppTemplate t) => JsonSerializer.Serialize(InfobipWhatsAppTemplateProvider.BuildRequest(t));

    [Fact]
    public void Infobip_request_has_body_examples_from_variables_or_samples()
    {
        var json = Request(Template(variables: "[\"Ann\",\"\"]"));
        Assert.Contains("\"category\":\"MARKETING\"", json);
        Assert.Contains("\"examples\":[\"Ann\",\"Sample 2\"]", json);
        Assert.Contains("\"language\":\"en_US\"", json);
        Assert.DoesNotContain("examples", Request(Template(body: "No placeholders")));
        Assert.Contains("Sample 1", Request(Template(body: "Hi {{1}}", variables: "not json")));
    }

    [Fact]
    public void Infobip_request_headers_footer_and_buttons()
    {
        var text = Request(Template(header: "text", headerContent: "Hi {{1}}", footer: "Bye"));
        Assert.Contains("\"header\":{\"format\":\"TEXT\",\"text\":\"Hi {{1}}\",\"example\":\"Sample\"}", text);
        Assert.Contains("\"footer\":{\"text\":\"Bye\"}", text);
        Assert.Contains("\"format\":\"VIDEO\"", Request(Template(header: "video", headerContent: "https://x/v.mp4")));

        var buttons = Request(Template(buttons: "[{\"type\":\"quick_reply\",\"text\":\"Yes\"},{\"type\":\"PHONE_NUMBER\",\"text\":\"Call\",\"phone_number\":\"+961\"},{\"type\":\"URL\",\"text\":\"Open\",\"url\":\"https://x/{{1}}\",\"example\":[\"https://x/1\"]},{\"type\":\"URL\",\"text\":\"Plain\",\"url\":\"https://x\"},{\"type\":\"OTHER\"}]"));
        Assert.Contains("\"type\":\"QUICK_REPLY\"", buttons);
        Assert.Contains("\"phoneNumber\":\"\\u002B961\"", buttons);
        Assert.Contains("\"example\":\"https://x/1\"", buttons);
        Assert.DoesNotContain("OTHER", buttons);
        Assert.DoesNotContain("\"buttons\"", Request(Template(buttons: "{\"not\":\"array\"}")));
    }

    [Theory]
    [InlineData(null, "PENDING")]
    [InlineData("", "PENDING")]
    [InlineData("unknown", "PENDING")]
    [InlineData("first_paused", "PAUSED")]
    [InlineData("SECOND_PAUSED", "PAUSED")]
    [InlineData("reinstated", "APPROVED")]
    [InlineData("rejected", "REJECTED")]
    public void Infobip_status_mapping(string? input, string expected) => Assert.Equal(expected, InfobipWhatsAppTemplateProvider.ToWhatsAppStatus(input));

    [Fact]
    public async Task Infobip_provider_submit_status_and_neutral_errors()
    {
        var provider = new InfobipWhatsAppTemplateProvider(_infobip.Object, NullLogger<InfobipWhatsAppTemplateProvider>.Instance);
        var integration = new ChannelIntegration { ProviderSenderId = "447000" };
        Assert.True(provider.IsReady(integration));
        Assert.False(provider.IsReady(new ChannelIntegration()));

        _infobip.Setup(c => c.CreateWhatsAppTemplateAsync("447000", It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync(new InfobipTemplateInfo("77", "FIRST_PAUSED"));
        var r = await provider.SubmitAsync(integration, Template());
        Assert.Equal(("77", "PAUSED"), (r.ProviderTemplateId, r.Status));

        _infobip.Setup(c => c.GetWhatsAppTemplateAsync("447000", "tpl-1", It.IsAny<CancellationToken>())).ReturnsAsync(new InfobipTemplateInfo("tpl-1", "APPROVED"));
        Assert.Equal("APPROVED", (await provider.GetStatusAsync(integration, Template())).Status);

        _infobip.Setup(c => c.CreateWhatsAppTemplateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InfobipApiException("Infobip rejected", 400) { Detail = "name invalid" });
        Assert.Contains("name invalid", (await Assert.ThrowsAsync<WhatsAppTemplateProviderException>(() => provider.SubmitAsync(integration, Template()))).Message);
        _infobip.Setup(c => c.CreateWhatsAppTemplateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InfobipApiException("x", 400) { Detail = "Infobip internal" });
        var neutral = await Assert.ThrowsAsync<WhatsAppTemplateProviderException>(() => provider.SubmitAsync(integration, Template()));
        Assert.DoesNotContain("nfobip", neutral.Message);
        _infobip.Setup(c => c.CreateWhatsAppTemplateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InfobipApiException("x", 503));
        Assert.Contains("Retry submit", (await Assert.ThrowsAsync<WhatsAppTemplateProviderException>(() => provider.SubmitAsync(integration, Template()))).Message);

        _infobip.Setup(c => c.GetWhatsAppTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InfobipApiException("x", 500));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetStatusAsync(integration, Template()));
    }
}

public class WhatsAppServiceAndSenderTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IWhatsAppProvider> _meta = new();
    private readonly Mock<IWhatsAppProvider> _infobip = new();

    public WhatsAppServiceAndSenderTests()
    {
        _meta.SetupGet(p => p.Name).Returns(ChannelProvider.Meta);
        _meta.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(true);
        _meta.Setup(p => p.SendTextAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("meta-id");
        _meta.Setup(p => p.SendTemplateAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<WhatsAppTemplateSend>(), It.IsAny<CancellationToken>())).ReturnsAsync("meta-tpl");
        _infobip.SetupGet(p => p.Name).Returns(ChannelProvider.Infobip);
        _infobip.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(true);
        _infobip.Setup(p => p.SendTextAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("ib-id");
    }

    private WhatsAppService Sut(string? provider = null) => new(_integrations.Object, [_meta.Object, _infobip.Object],
        new ConfigurationBuilder().AddInMemoryCollection(provider is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["WhatsApp:Provider"] = provider }).Build());

    private void Connected(string provider = ChannelProvider.Meta, string status = ChannelIntegrationStatus.Connected) =>
        _integrations.Setup(i => i.GetReadOnlyAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelIntegration { ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = status, Provider = provider });

    [Theory]
    [InlineData(null, "meta")]
    [InlineData("  ", "meta")]
    [InlineData(" INFOBIP ", "infobip")]
    public void Active_provider_name_is_normalised(string? configured, string expected) =>
        Assert.Equal(expected, WhatsAppService.ActiveProviderName(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = configured }).Build()));

    [Fact]
    public async Task Messages_go_through_the_active_provider()
    {
        Connected();
        Assert.Equal("meta-id", await Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
        Assert.Equal("meta-tpl", await Sut().SendTemplateMessageAsync(_clinicId, "961", "promo", "en", ["a"]));
        Connected(ChannelProvider.Infobip);
        Assert.Equal("ib-id", await Sut("infobip").SendTextMessageAsync(_clinicId, "961", "hi"));
    }

    [Fact]
    public async Task Sending_is_refused_when_not_connected_not_ready_or_on_another_provider()
    {
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi")); // no integration
        Connected(status: ChannelIntegrationStatus.Disconnected);
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
        Connected(ChannelProvider.Infobip);
        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi")); // connected via infobip, meta active
        Assert.Contains("different provider", ex.Message);
        Connected();
        _meta.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(false);
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut("carrier-pigeon").SendTextMessageAsync(_clinicId, "961", "hi"));
    }

    [Fact]
    public async Task Whatsapp_sender_enforces_the_24_hour_window_and_a_phone_number()
    {
        var whatsApp = new Mock<IWhatsAppService>();
        whatsApp.Setup(w => w.SendTextMessageAsync(_clinicId, "961", "hi", It.IsAny<CancellationToken>())).ReturnsAsync("sent");
        var sender = new WhatsAppChannelSender(whatsApp.Object);
        Assert.Equal(ConversationChannel.WhatsApp, sender.Channel);

        var closed = new Conversation { ClinicId = _clinicId, Channel = "whatsapp", Lead = new Lead { Phone = "961" } };
        await Assert.ThrowsAsync<ServiceWindowClosedException>(() => sender.SendTextAsync(closed, "hi"));
        var noPhone = new Conversation { ClinicId = _clinicId, Channel = "whatsapp", ServiceWindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Lead = new Lead() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTextAsync(noPhone, "hi"));
        var open = new Conversation { ClinicId = _clinicId, Channel = "whatsapp", ServiceWindowExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Lead = new Lead { Phone = "961" } };
        Assert.Equal("sent", await sender.SendTextAsync(open, "hi"));
    }

    [Fact]
    public async Task Telegram_sender_needs_a_chat_and_a_connected_bot_and_returns_a_composite_id()
    {
        var client = new Mock<ITelegramBotClient>();
        client.Setup(c => c.SendMessageAsync("bot-token", "42", "hi", It.IsAny<CancellationToken>())).ReturnsAsync(9);
        var sender = new TelegramChannelSender(_integrations.Object, client.Object);
        Assert.Equal(ConversationChannel.Telegram, sender.Channel);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTextAsync(new Conversation { ClinicId = _clinicId }, "hi"));
        var convo = new Conversation { ClinicId = _clinicId, ExternalThreadId = "42" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTextAsync(convo, "hi")); // no integration
        _integrations.Setup(i => i.GetReadOnlyAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelIntegration { Status = ChannelIntegrationStatus.Disconnected, AccessToken = "bot-token" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTextAsync(convo, "hi"));
        _integrations.Setup(i => i.GetReadOnlyAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "bot-token" });
        Assert.Equal("42:9", await sender.SendTextAsync(convo, "hi"));
    }
}

public class WebsiteFetchClientTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IConfigManager> _config = new();

    public WebsiteFetchClientTests()
    {
        _config.SetupGet(c => c.WebScrapingMaxRedirects).Returns(3);
        _config.SetupGet(c => c.WebScrapingRequestTimeoutSeconds).Returns(5);
        _config.SetupGet(c => c.WebScrapingMaxResponseBytes).Returns(1000);
        _config.SetupGet(c => c.WebScrapingUserAgent).Returns("SculptFlowBot/1.0 (+https://example.com)");
        _config.SetupGet(c => c.WebScrapingDevAllowedHosts).Returns("");
    }

    private WebsiteFetchClient Sut()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return new WebsiteFetchClient(_http.CreateClient(), new SsrfGuard(_config.Object, env.Object), _config.Object, NullLogger<WebsiteFetchClient>.Instance);
    }

    private static readonly Uri Page = new("https://8.8.8.8/page");
    private static bool SameSite(Uri u) => u.Host == "8.8.8.8";

    [Fact]
    public async Task A_successful_html_fetch_returns_the_decoded_body_and_validators()
    {
        _http.Enqueue(HttpStatusCode.OK, "<html>héllo</html>", "text/html; charset=utf-8", r => { r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\""); r.Content.Headers.LastModified = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero); });
        var r = await Sut().FetchPageAsync(Page, "\"old\"", "Mon, 01 Jan 2026 00:00:00 GMT", SameSite);
        Assert.True(r.Ok);
        Assert.Equal(("<html>héllo</html>", "\"v1\"", "text/html"), (r.Html, r.ETag, r.ContentType));
        Assert.NotNull(r.LastModified);
        var req = _http.Requests[0];
        Assert.Equal("\"old\"", req.Headers["If-None-Match"]);
        Assert.Contains("SculptFlowBot", req.Headers["User-Agent"]);
    }

    [Fact]
    public async Task Not_modified_keeps_the_validators_and_errors_are_classified()
    {
        _http.Enqueue(HttpStatusCode.NotModified);
        var nm = await Sut().FetchPageAsync(Page, "\"e\"", "lm", SameSite);
        Assert.True(nm.NotModified);
        Assert.Equal("\"e\"", nm.ETag);

        _http.Enqueue(HttpStatusCode.NotFound);
        var nf = await Sut().FetchPageAsync(Page, null, null, SameSite);
        Assert.Equal((FetchErrorKind.HttpError, 404), (nf.ErrorKind, nf.StatusCode));

        _http.Enqueue(HttpStatusCode.OK, "{}", "application/json");
        Assert.Equal(FetchErrorKind.NotHtml, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
    }

    [Fact]
    public async Task Oversized_pages_are_rejected_by_header_and_by_streaming()
    {
        _http.Enqueue(HttpStatusCode.OK, new string('x', 5000), "text/html");
        Assert.Equal(FetchErrorKind.TooLarge, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
        _http.Enqueue(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 5000)))) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html") } } });
        Assert.Equal(FetchErrorKind.TooLarge, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
    }

    [Fact]
    public async Task Same_site_redirects_are_followed_and_conditional_headers_only_sent_on_the_first_hop()
    {
        _http.Enqueue(HttpStatusCode.MovedPermanently, "", configure: r => r.Headers.Location = new Uri("/new", UriKind.Relative));
        _http.Enqueue(HttpStatusCode.OK, "<html>ok</html>", "text/html");
        var r = await Sut().FetchPageAsync(Page, "\"e\"", null, SameSite);
        Assert.Equal("https://8.8.8.8/new", r.FinalUrl);
        Assert.True(_http.Requests[0].Headers.ContainsKey("If-None-Match"));
        Assert.False(_http.Requests[1].Headers.ContainsKey("If-None-Match"));
    }

    [Fact]
    public async Task Redirects_off_site_without_location_or_in_loops_are_reported()
    {
        _http.Enqueue(HttpStatusCode.Found, "", configure: r => r.Headers.Location = new Uri("https://evil.example/x"));
        Assert.Equal(FetchErrorKind.ExternalRedirect, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
        _http.Enqueue(HttpStatusCode.Found);
        Assert.Equal(FetchErrorKind.HttpError, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
        _http.Always(_ => { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new Uri("/again", UriKind.Relative); return r; });
        Assert.Equal(FetchErrorKind.TooManyRedirects, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
    }

    [Fact]
    public async Task Unsafe_targets_and_network_errors_never_throw()
    {
        var blocked = await Sut().FetchPageAsync(new Uri("http://127.0.0.1/"), null, null, _ => true);
        Assert.Equal(FetchErrorKind.Blocked, blocked.ErrorKind);
        Assert.Empty(_http.Requests);

        _http.EnqueueThrow(new HttpRequestException("dns"));
        Assert.Equal(FetchErrorKind.Network, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
        _http.EnqueueThrow(new HttpRequestException("wrapped", new UnsafeUrlException("private address")));
        Assert.Equal(FetchErrorKind.Blocked, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
        _http.EnqueueThrow(new TaskCanceledException("timeout"));
        Assert.Equal(FetchErrorKind.Timeout, (await Sut().FetchPageAsync(Page, null, null, SameSite)).ErrorKind);
    }

    [Fact]
    public async Task Robots_fetch_distinguishes_unavailable_missing_and_present()
    {
        _http.Enqueue(HttpStatusCode.OK, "User-agent: *\nDisallow: /private\nCrawl-delay: 2");
        var present = await Sut().FetchRobotsAsync(new Uri("https://8.8.8.8/some/page"));
        Assert.False(present.Unavailable);
        Assert.False(present.Robots!.IsAllowed("/private"));
        Assert.Equal(2, present.Robots.CrawlDelaySeconds);
        Assert.Equal("https://8.8.8.8/robots.txt", _http.Requests[0].Uri.ToString());

        _http.Enqueue(HttpStatusCode.NotFound);
        var missing = await Sut().FetchRobotsAsync(new Uri("https://8.8.8.8/"));
        Assert.False(missing.Unavailable);
        Assert.True(missing.Robots!.IsAllowed("/anything"));

        _http.Enqueue(HttpStatusCode.ServiceUnavailable);
        Assert.True((await Sut().FetchRobotsAsync(new Uri("https://8.8.8.8/"))).Unavailable);
        _http.EnqueueThrow(new HttpRequestException("down"));
        Assert.True((await Sut().FetchRobotsAsync(new Uri("https://8.8.8.8/"))).Unavailable);
        _http.EnqueueThrow(new TaskCanceledException("t"));
        Assert.Contains("timed out", (await Sut().FetchRobotsAsync(new Uri("https://8.8.8.8/"))).Note);
        var blocked = await Sut().FetchRobotsAsync(new Uri("http://10.0.0.1/"));
        Assert.True(blocked.Unavailable);
    }
}
