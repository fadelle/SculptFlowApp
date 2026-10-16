using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.WebScraping;
using Omni.App.Business.HttpClients.WebScraping;
using Omni.App.Business.Providers.Channels;
using Omni.App.Business.Providers.WhatsApp;
using Omni.App.Business.Services.Inbox;
using Omni.App.Tests.Support;
using Microsoft.Extensions.Hosting;

namespace Omni.App.Tests.Providers;

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

public class WhatsAppTemplateProviderTests
{
    private readonly Mock<IMetaGraphClient> _meta = new();

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

}

public class WhatsAppServiceAndSenderTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IWhatsAppProvider> _meta = new();

    public WhatsAppServiceAndSenderTests()
    {
        _meta.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(true);
        _meta.Setup(p => p.SendTextAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("meta-id");
        _meta.Setup(p => p.SendTemplateAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<WhatsAppTemplateSend>(), It.IsAny<CancellationToken>())).ReturnsAsync("meta-tpl");
    }

    private WhatsAppService Sut() => new(_integrations.Object, _meta.Object);

    private void Connected(string status = ChannelIntegrationStatus.Connected) =>
        _integrations.Setup(i => i.GetReadOnlyAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelIntegration { ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = status });

    [Fact]
    public async Task Messages_go_through_the_meta_provider()
    {
        Connected();
        Assert.Equal("meta-id", await Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
        Assert.Equal("meta-tpl", await Sut().SendTemplateMessageAsync(_clinicId, "961", "promo", "en", ["a"]));
    }

    [Fact]
    public async Task Sending_is_refused_when_not_connected_or_not_ready()
    {
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi")); // no integration
        Connected(status: ChannelIntegrationStatus.Disconnected);
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
        Connected();
        _meta.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(false);
        await Assert.ThrowsAsync<WhatsAppSendException>(() => Sut().SendTextMessageAsync(_clinicId, "961", "hi"));
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
        _config.SetupGet(c => c.WebScrapingUserAgent).Returns("OmniBot/1.0 (+https://example.com)");
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
        Assert.Contains("OmniBot", req.Headers["User-Agent"]);
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
