using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Services.Channels;

namespace Omni.App.Tests.Services;

public class ChannelIntegrationServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMetaGraphClient> _graph = new();
    private readonly Mock<ITelegramIntegrationService> _telegram = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<ICacheManager> _cache = new();

    private ChannelIntegrationService Sut() => new(_repo.Object, _uow.Object, _graph.Object, _telegram.Object, _entitlements.Object, _cache.Object);

    private static SaveChannelIntegrationRequest Save(Guid client, string channel = ChannelType.Facebook, string? token = "tok", string? verify = null, string? pin = null) =>
        new(client, channel, "Page", "pn", "waba", "page1", "ig1", token, verify, pin);

    [Fact]
    public async Task List_returns_every_channel_with_disconnected_placeholders()
    {
        var wa = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, AccessToken = "x" };
        _repo.Setup(r => r.ListForClientAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration> { wa });
        var list = await Sut().ListAsync(_clientId);
        Assert.Equal(ChannelType.All.Count, list.Count);
        var w = list.Single(l => l.Channel == ChannelType.WhatsApp);
        Assert.True(w.HasAccessToken);
        Assert.Equal(ChannelIntegrationStatus.Disconnected, list.First(l => l.Channel != ChannelType.WhatsApp).Status);
    }

    [Fact]
    public async Task Save_validates_channel_and_rejects_the_manual_telegram_form()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(Save(_clientId, "pigeon")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(Save(_clientId, ChannelType.Telegram)));
    }

    [Fact]
    public async Task Save_creates_a_connected_row_checks_entitlements_and_clears_routing_cache()
    {
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        var r = await Sut().SaveAsync(Save(_clientId, ChannelType.WhatsApp, "tok", "verify", "123456"));
        Assert.Equal((ChannelIntegrationStatus.Connected, "tok", "verify", "123456"), (added!.Status, added.AccessToken, added.WebhookVerifyToken, added.Pin));
        Assert.True(r.HasAccessToken);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_without_any_token_stays_disconnected_and_skips_the_entitlement_check()
    {
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        await Sut().SaveAsync(Save(_clientId, ChannelType.Instagram, token: " "));
        Assert.Equal(ChannelIntegrationStatus.Disconnected, added!.Status);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_keeps_existing_secrets_when_the_form_leaves_them_blank()
    {
        var row = new ChannelIntegration { ClientId = _clientId, Channel = ChannelType.Facebook, AccessToken = "old", WebhookVerifyToken = "oldv", Pin = "999999" };
        _repo.Setup(r => r.GetAsync(_clientId, ChannelType.Facebook, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Sut().SaveAsync(Save(_clientId, token: "", verify: " ", pin: null));
        Assert.Equal(("old", "oldv", "999999", ChannelIntegrationStatus.Connected), (row.AccessToken, row.WebhookVerifyToken, row.Pin, row.Status));
        _repo.Verify(r => r.Add(It.IsAny<ChannelIntegration>()), Times.Never);
    }

    [Fact]
    public async Task Disconnect_clears_secrets_and_routes_telegram_to_its_own_service()
    {
        await Sut().DisconnectAsync(_clientId, ChannelType.Telegram);
        _telegram.Verify(t => t.DisconnectAsync(_clientId, It.IsAny<CancellationToken>()), Times.Once);

        await Sut().DisconnectAsync(_clientId, ChannelType.Facebook); // nothing stored: no-op
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var row = new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "a", WebhookVerifyToken = "w", Pin = "1", LastError = "e" };
        _repo.Setup(r => r.GetAsync(_clientId, ChannelType.Facebook, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Sut().DisconnectAsync(_clientId, ChannelType.Facebook);
        Assert.Equal((ChannelIntegrationStatus.Disconnected, null, null, null, null), (row.Status, row.AccessToken, row.WebhookVerifyToken, row.Pin, row.LastError));
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disconnect_rejects_an_unknown_channel_type()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().DisconnectAsync(_clientId, "pigeon"));
        _repo.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectWhatsApp_with_a_code_discovers_waba_and_number_registers_and_saves()
    {
        _graph.Setup(g => g.ExchangeCodeForTokenAsync("code", "https://r", It.IsAny<CancellationToken>())).ReturnsAsync("tok");
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo> { new("b", "Biz", "W1", "WABA", "owner") });
        _graph.Setup(g => g.GetPhoneNumbersForWabaAsync("W1", "tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WabaPhoneNumberInfo> { new("P1", "+961 70", "Verified") });
        _graph.Setup(g => g.GetWhatsAppPhoneNumberAsync("P1", "tok", It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppPhoneNumberInfo("+961 70 000 000", "Verified"));
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);

        var r = await Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId, Code: "code", RedirectUri: "https://r"));

        Assert.Equal(("P1", "W1", "+961 70 000 000", "tok"), (added!.PhoneNumberId, added.WhatsAppBusinessId, added.DisplayName, added.AccessToken));
        Assert.Matches(@"^\d{6}$", added.Pin);
        _graph.Verify(g => g.RegisterPhoneNumberAsync("P1", "tok", added.Pin!, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ChannelIntegrationStatus.Connected, r.Status);
    }

    [Fact]
    public async Task ConnectWhatsApp_with_explicit_ids_skips_discovery()
    {
        await Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId, AccessToken: "tok", WabaId: "W9", PhoneNumberId: "P9"));
        _graph.Verify(g => g.GetClientWhatsAppBusinessAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _graph.Verify(g => g.RegisterPhoneNumberAsync("P9", "tok", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectWhatsApp_error_paths()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId)));
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId, AccessToken: "tok")));
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo> { new("b", "Biz", "W1", "WABA", "o") });
        _graph.Setup(g => g.GetPhoneNumbersForWabaAsync("W1", "tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WabaPhoneNumberInfo>());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId, AccessToken: "tok")));
        Assert.Contains("WABA", TextKit.English(ex));
        _entitlements.Setup(e => e.EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("x", "limit"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clientId, AccessToken: "tok")));
    }

    [Fact]
    public async Task ConnectFacebook_exchanges_the_code_upgrades_the_token_and_stores_the_page_token()
    {
        _graph.Setup(g => g.ExchangeCodeForTokenAsync("c", null, It.IsAny<CancellationToken>())).ReturnsAsync("short");
        _graph.Setup(g => g.GetLongLivedTokenAsync("short", It.IsAny<CancellationToken>())).ReturnsAsync("long");
        _graph.Setup(g => g.GetFirstManagedPageAsync("long", It.IsAny<CancellationToken>())).ReturnsAsync(new FacebookPageInfo("pg", "My Page", "page-token"));
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        await Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clientId, "c", null));
        Assert.Equal(("pg", "page-token", "My Page"), (added!.PageId, added.AccessToken, added.DisplayName));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clientId, null, null)));
        _graph.Setup(g => g.GetFirstManagedPageAsync("long", It.IsAny<CancellationToken>())).ReturnsAsync((FacebookPageInfo?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clientId, null, "short")));
    }

    [Fact]
    public void Response_never_exposes_secrets()
    {
        var wa = new ChannelIntegration { Channel = ChannelType.WhatsApp, AccessToken = "secret", WebhookVerifyToken = "v" };
        var r = ChannelIntegrationService.ToResponse(wa);
        Assert.True(r.HasAccessToken);
        Assert.True(r.HasWebhookVerifyToken);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(r));
    }
}

public class TelegramIntegrationServiceTests
{
    private const string GoodToken = "123456789:AAEhBOweik6ad8h_s0m3Th1ngLongEnoughToPass";
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITelegramBotClient> _client = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly IHttpContextAccessor _http = new HttpContextAccessor();
    private IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://app.example.com/" }).Build();
    private ChannelIntegration? _row;

    public TelegramIntegrationServiceTests()
    {
        _client.Setup(c => c.GetMeAsync(GoodToken, It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramBotInfo(555, "client_bot", "Client"));
        _client.Setup(c => c.GetWebhookInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TelegramWebhookInfo($"https://app.example.com/api/integrations/telegram/webhook/{_row?.Id}", 0, null, null));
        _repo.Setup(r => r.GetAsync(_clientId, ChannelType.Telegram, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => _row = c);
    }

    private TelegramIntegrationService Sut() => new(_repo.Object, _uow.Object, _client.Object, _configuration, _http, NullLogger<TelegramIntegrationService>.Instance, _entitlements.Object);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("123:short")]
    [InlineData("\"123456789:AAEhBOweik6ad8h_s0m3Th1ngLongEnoughToPass\"")]
    public async Task Bad_tokens_are_rejected_before_any_call(string? token)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ConnectAsync(_clientId, token));
        _client.Verify(c => c.GetMeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Connect_registers_the_webhook_with_a_secret_and_stores_the_bot()
    {
        var r = await Sut().ConnectAsync(_clientId, $" {GoodToken} ");
        Assert.Equal(("555", "client_bot", "@client_bot", ChannelIntegrationStatus.Connected, WebhookStatus.Active), (_row!.TelegramBotId, _row.TelegramBotUsername, _row.DisplayName, _row.Status, _row.WebhookStatus));
        Assert.False(string.IsNullOrEmpty(_row.WebhookVerifyToken));
        _client.Verify(c => c.SetWebhookAsync(GoodToken, $"https://app.example.com/api/integrations/telegram/webhook/{_row.Id}", _row.WebhookVerifyToken!, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ChannelIntegrationStatus.Connected, r.Status);
        Assert.Null(_row.LastError);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(_clientId, ChannelType.Telegram, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connect_refuses_a_bot_used_by_another_client_and_a_non_https_base_url()
    {
        _repo.Setup(r => r.IsTelegramBotConnectedElsewhereAsync("555", _clientId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clientId, GoodToken));
        Assert.Contains("another business", TextKit.English(ex));
        _client.Verify(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "http://insecure.example.com" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clientId, GoodToken));
    }

    [Fact]
    public async Task Without_a_configured_url_a_public_request_host_is_used_but_localhost_is_not()
    {
        _configuration = new ConfigurationBuilder().Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clientId, GoodToken));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("localhost", 5000);
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var local = new TelegramIntegrationService(_repo.Object, _uow.Object, _client.Object, _configuration, accessor, NullLogger<TelegramIntegrationService>.Instance, _entitlements.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => local.ConnectAsync(_clientId, GoodToken));

        ctx.Request.Host = new HostString("client.onrender.com");
        await local.ConnectAsync(_clientId, GoodToken);
        _client.Verify(c => c.SetWebhookAsync(GoodToken, It.Is<string>(u => u.StartsWith("https://client.onrender.com/api/integrations/telegram/webhook/")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconnecting_with_a_different_bot_removes_the_old_webhook_first()
    {
        _row = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, AccessToken = "old-token", TelegramBotId = "111" };
        await Sut().ConnectAsync(_clientId, GoodToken);
        _client.Verify(c => c.DeleteWebhookAsync("old-token", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("555", _row.TelegramBotId);

        _client.Invocations.Clear();
        await Sut().ConnectAsync(_clientId, GoodToken); // same bot again: nothing to delete
        _client.Verify(c => c.DeleteWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_reports_health_from_telegrams_webhook_info()
    {
        var empty = await Sut().RefreshStatusAsync(_clientId);
        Assert.Equal((Guid.Empty, ChannelIntegrationStatus.Disconnected), (empty.Id, empty.Status));

        _row = new ChannelIntegration { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, AccessToken = "t" };
        await Sut().RefreshStatusAsync(_clientId);
        Assert.Equal((WebhookStatus.Active, null), (_row.WebhookStatus, _row.LastError));

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo("https://elsewhere/hook", 0, null, null));
        await Sut().RefreshStatusAsync(_clientId);
        Assert.Equal(WebhookStatus.Error, _row.WebhookStatus);
        Assert.Contains("Reconnect", _row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo($"https://x/api/integrations/telegram/webhook/{_row.Id}", 3, "Connection timed out", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await Sut().RefreshStatusAsync(_clientId);
        Assert.Contains("timed out", _row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo($"https://x/api/integrations/telegram/webhook/{_row.Id}", 0, "old error", DateTimeOffset.UtcNow.AddHours(-3)));
        await Sut().RefreshStatusAsync(_clientId);
        Assert.Null(_row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ThrowsAsync(new TelegramApiException("Unauthorized"));
        await Sut().RefreshStatusAsync(_clientId);
        Assert.Equal((WebhookStatus.Error, "Unauthorized"), (_row.WebhookStatus, _row.LastError));
    }

    [Fact]
    public async Task Disconnect_removes_the_webhook_and_tolerates_telegram_failing()
    {
        await Sut().DisconnectAsync(_clientId);
        _row = new ChannelIntegration { Id = Guid.NewGuid(), Status = ChannelIntegrationStatus.Connected, AccessToken = "t", WebhookVerifyToken = "s" };
        await Sut().DisconnectAsync(_clientId);
        _client.Verify(c => c.DeleteWebhookAsync("t", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((ChannelIntegrationStatus.Disconnected, null, null, WebhookStatus.NotRegistered, null), (_row.Status, _row.AccessToken, _row.WebhookVerifyToken, _row.WebhookStatus, _row.LastError));

        _row = new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "t" };
        _client.Setup(c => c.DeleteWebhookAsync("t", It.IsAny<CancellationToken>())).ThrowsAsync(new TelegramApiException("boom"));
        await Sut().DisconnectAsync(_clientId);
        Assert.Equal(ChannelIntegrationStatus.Disconnected, _row.Status);
        Assert.Contains("couldn't be told", _row.LastError);
    }
}

