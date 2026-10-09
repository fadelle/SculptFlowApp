using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Services.Channels;

namespace PlasticSurgery.Tests.Services;

public class ChannelIntegrationServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMetaGraphClient> _graph = new();
    private readonly Mock<ITelegramIntegrationService> _telegram = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<ICacheManager> _cache = new();

    private ChannelIntegrationService Sut() => new(_repo.Object, _uow.Object, _graph.Object, _telegram.Object, _entitlements.Object, _cache.Object);

    private static SaveChannelIntegrationRequest Save(Guid clinic, string channel = ChannelType.Facebook, string? token = "tok", string? verify = null, string? pin = null) =>
        new(clinic, channel, "Page", "pn", "waba", "page1", "ig1", token, verify, pin);

    [Fact]
    public async Task List_returns_every_channel_with_disconnected_placeholders()
    {
        var wa = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, AccessToken = "x" };
        _repo.Setup(r => r.ListForClinicAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegration> { wa });
        var list = await Sut().ListAsync(_clinicId);
        Assert.Equal(ChannelType.All.Count, list.Count);
        var w = list.Single(l => l.Channel == ChannelType.WhatsApp);
        Assert.True(w.HasAccessToken);
        Assert.Equal(ChannelIntegrationStatus.Disconnected, list.First(l => l.Channel != ChannelType.WhatsApp).Status);
    }

    [Fact]
    public async Task Save_validates_channel_and_rejects_the_manual_telegram_form()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(Save(_clinicId, "pigeon")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(Save(_clinicId, ChannelType.Telegram)));
    }

    [Fact]
    public async Task Save_creates_a_connected_row_checks_entitlements_and_clears_routing_cache()
    {
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        var r = await Sut().SaveAsync(Save(_clinicId, ChannelType.WhatsApp, "tok", "verify", "123456"));
        Assert.Equal((ChannelIntegrationStatus.Connected, ChannelProvider.Meta, "tok", "verify", "123456"), (added!.Status, added.Provider, added.AccessToken, added.WebhookVerifyToken, added.Pin));
        Assert.Null(added.ProviderSenderId);
        Assert.True(r.HasAccessToken);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_without_any_token_stays_disconnected_and_skips_the_entitlement_check()
    {
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        await Sut().SaveAsync(Save(_clinicId, ChannelType.Instagram, token: " "));
        Assert.Equal(ChannelIntegrationStatus.Disconnected, added!.Status);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_keeps_existing_secrets_when_the_form_leaves_them_blank()
    {
        var row = new ChannelIntegration { ClinicId = _clinicId, Channel = ChannelType.Facebook, AccessToken = "old", WebhookVerifyToken = "oldv", Pin = "999999" };
        _repo.Setup(r => r.GetAsync(_clinicId, ChannelType.Facebook, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Sut().SaveAsync(Save(_clinicId, token: "", verify: " ", pin: null));
        Assert.Equal(("old", "oldv", "999999", ChannelIntegrationStatus.Connected), (row.AccessToken, row.WebhookVerifyToken, row.Pin, row.Status));
        _repo.Verify(r => r.Add(It.IsAny<ChannelIntegration>()), Times.Never);
    }

    [Fact]
    public async Task Disconnect_clears_secrets_and_routes_telegram_to_its_own_service()
    {
        await Sut().DisconnectAsync(_clinicId, ChannelType.Telegram);
        _telegram.Verify(t => t.DisconnectAsync(_clinicId, It.IsAny<CancellationToken>()), Times.Once);

        await Sut().DisconnectAsync(_clinicId, ChannelType.Facebook); // nothing stored: no-op
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var row = new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "a", WebhookVerifyToken = "w", Pin = "1", LastError = "e" };
        _repo.Setup(r => r.GetAsync(_clinicId, ChannelType.Facebook, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        await Sut().DisconnectAsync(_clinicId, ChannelType.Facebook);
        Assert.Equal((ChannelIntegrationStatus.Disconnected, null, null, null, null), (row.Status, row.AccessToken, row.WebhookVerifyToken, row.Pin, row.LastError));
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
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

        var r = await Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId, Code: "code", RedirectUri: "https://r"));

        Assert.Equal(("P1", "W1", "+961 70 000 000", "tok"), (added!.PhoneNumberId, added.WhatsAppBusinessId, added.DisplayName, added.AccessToken));
        Assert.Matches(@"^\d{6}$", added.Pin);
        _graph.Verify(g => g.RegisterPhoneNumberAsync("P1", "tok", added.Pin!, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ChannelIntegrationStatus.Connected, r.Status);
    }

    [Fact]
    public async Task ConnectWhatsApp_with_explicit_ids_skips_discovery()
    {
        await Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId, AccessToken: "tok", WabaId: "W9", PhoneNumberId: "P9"));
        _graph.Verify(g => g.GetClientWhatsAppBusinessAccountsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _graph.Verify(g => g.RegisterPhoneNumberAsync("P9", "tok", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectWhatsApp_error_paths()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId)));
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId, AccessToken: "tok")));
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo> { new("b", "Biz", "W1", "WABA", "o") });
        _graph.Setup(g => g.GetPhoneNumbersForWabaAsync("W1", "tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<WabaPhoneNumberInfo>());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId, AccessToken: "tok")));
        Assert.Contains("WABA", ex.Message);
        _entitlements.Setup(e => e.EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("x", "limit"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ConnectWhatsAppAsync(new ConnectWhatsAppRequest(_clinicId, AccessToken: "tok")));
    }

    [Fact]
    public async Task ConnectFacebook_exchanges_the_code_upgrades_the_token_and_stores_the_page_token()
    {
        _graph.Setup(g => g.ExchangeCodeForTokenAsync("c", null, It.IsAny<CancellationToken>())).ReturnsAsync("short");
        _graph.Setup(g => g.GetLongLivedTokenAsync("short", It.IsAny<CancellationToken>())).ReturnsAsync("long");
        _graph.Setup(g => g.GetFirstManagedPageAsync("long", It.IsAny<CancellationToken>())).ReturnsAsync(new FacebookPageInfo("pg", "My Page", "page-token"));
        ChannelIntegration? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => added = c);
        await Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clinicId, "c", null));
        Assert.Equal(("pg", "page-token", "My Page"), (added!.PageId, added.AccessToken, added.DisplayName));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clinicId, null, null)));
        _graph.Setup(g => g.GetFirstManagedPageAsync("long", It.IsAny<CancellationToken>())).ReturnsAsync((FacebookPageInfo?)null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectFacebookAsync(new ConnectFacebookRequest(_clinicId, null, "short")));
    }

    [Fact]
    public void Response_never_exposes_secrets_and_shows_provider_only_for_whatsapp()
    {
        var wa = new ChannelIntegration { Channel = ChannelType.WhatsApp, AccessToken = "secret", WebhookVerifyToken = "v", Provider = ChannelProvider.Infobip };
        var r = ChannelIntegrationService.ToResponse(wa);
        Assert.True(r.HasAccessToken);
        Assert.True(r.HasWebhookVerifyToken);
        Assert.Equal(ChannelProvider.Infobip, r.Provider);
        Assert.Null(ChannelIntegrationService.ToResponse(new ChannelIntegration { Channel = ChannelType.Facebook }).Provider);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(r));
    }
}

public class TelegramIntegrationServiceTests
{
    private const string GoodToken = "123456789:AAEhBOweik6ad8h_s0m3Th1ngLongEnoughToPass";
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITelegramBotClient> _client = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly IHttpContextAccessor _http = new HttpContextAccessor();
    private IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://app.example.com/" }).Build();
    private ChannelIntegration? _row;

    public TelegramIntegrationServiceTests()
    {
        _client.Setup(c => c.GetMeAsync(GoodToken, It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramBotInfo(555, "clinic_bot", "Clinic"));
        _client.Setup(c => c.GetWebhookInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TelegramWebhookInfo($"https://app.example.com/api/integrations/telegram/webhook/{_row?.Id}", 0, null, null));
        _repo.Setup(r => r.GetAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
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
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ConnectAsync(_clinicId, token));
        _client.Verify(c => c.GetMeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Connect_registers_the_webhook_with_a_secret_and_stores_the_bot()
    {
        var r = await Sut().ConnectAsync(_clinicId, $" {GoodToken} ");
        Assert.Equal(("555", "clinic_bot", "@clinic_bot", ChannelIntegrationStatus.Connected, WebhookStatus.Active), (_row!.TelegramBotId, _row.TelegramBotUsername, _row.DisplayName, _row.Status, _row.WebhookStatus));
        Assert.False(string.IsNullOrEmpty(_row.WebhookVerifyToken));
        _client.Verify(c => c.SetWebhookAsync(GoodToken, $"https://app.example.com/api/integrations/telegram/webhook/{_row.Id}", _row.WebhookVerifyToken!, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ChannelIntegrationStatus.Connected, r.Status);
        Assert.Null(_row.LastError);
        _entitlements.Verify(e => e.EnsureCanConnectChannelAsync(_clinicId, ChannelType.Telegram, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connect_refuses_a_bot_used_by_another_clinic_and_a_non_https_base_url()
    {
        _repo.Setup(r => r.IsTelegramBotConnectedElsewhereAsync("555", _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, GoodToken));
        Assert.Contains("another clinic", ex.Message);
        _client.Verify(c => c.SetWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "http://insecure.example.com" }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, GoodToken));
    }

    [Fact]
    public async Task Without_a_configured_url_a_public_request_host_is_used_but_localhost_is_not()
    {
        _configuration = new ConfigurationBuilder().Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, GoodToken));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("localhost", 5000);
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        var local = new TelegramIntegrationService(_repo.Object, _uow.Object, _client.Object, _configuration, accessor, NullLogger<TelegramIntegrationService>.Instance, _entitlements.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => local.ConnectAsync(_clinicId, GoodToken));

        ctx.Request.Host = new HostString("clinic.onrender.com");
        await local.ConnectAsync(_clinicId, GoodToken);
        _client.Verify(c => c.SetWebhookAsync(GoodToken, It.Is<string>(u => u.StartsWith("https://clinic.onrender.com/api/integrations/telegram/webhook/")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reconnecting_with_a_different_bot_removes_the_old_webhook_first()
    {
        _row = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, AccessToken = "old-token", TelegramBotId = "111" };
        await Sut().ConnectAsync(_clinicId, GoodToken);
        _client.Verify(c => c.DeleteWebhookAsync("old-token", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("555", _row.TelegramBotId);

        _client.Invocations.Clear();
        await Sut().ConnectAsync(_clinicId, GoodToken); // same bot again: nothing to delete
        _client.Verify(c => c.DeleteWebhookAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_reports_health_from_telegrams_webhook_info()
    {
        var empty = await Sut().RefreshStatusAsync(_clinicId);
        Assert.Equal((Guid.Empty, ChannelIntegrationStatus.Disconnected), (empty.Id, empty.Status));

        _row = new ChannelIntegration { Id = Guid.NewGuid(), ClinicId = _clinicId, Channel = ChannelType.Telegram, Status = ChannelIntegrationStatus.Connected, AccessToken = "t" };
        await Sut().RefreshStatusAsync(_clinicId);
        Assert.Equal((WebhookStatus.Active, null), (_row.WebhookStatus, _row.LastError));

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo("https://elsewhere/hook", 0, null, null));
        await Sut().RefreshStatusAsync(_clinicId);
        Assert.Equal(WebhookStatus.Error, _row.WebhookStatus);
        Assert.Contains("Reconnect", _row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo($"https://x/api/integrations/telegram/webhook/{_row.Id}", 3, "Connection timed out", DateTimeOffset.UtcNow.AddMinutes(-5)));
        await Sut().RefreshStatusAsync(_clinicId);
        Assert.Contains("timed out", _row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ReturnsAsync(new TelegramWebhookInfo($"https://x/api/integrations/telegram/webhook/{_row.Id}", 0, "old error", DateTimeOffset.UtcNow.AddHours(-3)));
        await Sut().RefreshStatusAsync(_clinicId);
        Assert.Null(_row.LastError);

        _client.Setup(c => c.GetWebhookInfoAsync("t", It.IsAny<CancellationToken>())).ThrowsAsync(new TelegramApiException("Unauthorized"));
        await Sut().RefreshStatusAsync(_clinicId);
        Assert.Equal((WebhookStatus.Error, "Unauthorized"), (_row.WebhookStatus, _row.LastError));
    }

    [Fact]
    public async Task Disconnect_removes_the_webhook_and_tolerates_telegram_failing()
    {
        await Sut().DisconnectAsync(_clinicId);
        _row = new ChannelIntegration { Id = Guid.NewGuid(), Status = ChannelIntegrationStatus.Connected, AccessToken = "t", WebhookVerifyToken = "s" };
        await Sut().DisconnectAsync(_clinicId);
        _client.Verify(c => c.DeleteWebhookAsync("t", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((ChannelIntegrationStatus.Disconnected, null, null, WebhookStatus.NotRegistered, null), (_row.Status, _row.AccessToken, _row.WebhookVerifyToken, _row.WebhookStatus, _row.LastError));

        _row = new ChannelIntegration { Status = ChannelIntegrationStatus.Connected, AccessToken = "t" };
        _client.Setup(c => c.DeleteWebhookAsync("t", It.IsAny<CancellationToken>())).ThrowsAsync(new TelegramApiException("boom"));
        await Sut().DisconnectAsync(_clinicId);
        Assert.Equal(ChannelIntegrationStatus.Disconnected, _row.Status);
        Assert.Contains("couldn't be told", _row.LastError);
    }
}

public class InfobipWhatsAppIntegrationServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInfobipClient> _client = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<ICacheManager> _cache = new();
    private IConfiguration _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = "infobip", ["App:PublicBaseUrl"] = "https://app.example.com" }).Build();
    private ChannelIntegration? _row;

    public InfobipWhatsAppIntegrationServiceTests()
    {
        _client.SetupGet(c => c.IsConfigured).Returns(true);
        _client.Setup(c => c.GetWhatsAppSenderAsync("447860099299", It.IsAny<CancellationToken>())).ReturnsAsync(new InfobipSenderInfo("447860099299", "Clinic Name"));
        _repo.Setup(r => r.GetAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _repo.Setup(r => r.GetReadOnlyAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _repo.Setup(r => r.Add(It.IsAny<ChannelIntegration>())).Callback<ChannelIntegration>(c => _row = c);
    }

    private InfobipWhatsAppIntegrationService Sut() => new(_repo.Object, _uow.Object, _client.Object, _configuration, new HttpContextAccessor(),
        NullLogger<InfobipWhatsAppIntegrationService>.Instance, _entitlements.Object, _cache.Object);

    [Theory]
    [InlineData(null)]
    [InlineData("1234567")]
    [InlineData("1234567890123456")]
    public async Task Numbers_of_the_wrong_length_are_rejected(string? number) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ConnectAsync(_clinicId, number));

    [Fact]
    public async Task Connect_normalises_the_number_creates_a_secret_and_clears_the_meta_fields()
    {
        var r = await Sut().ConnectAsync(_clinicId, "+44 7860 099299");
        Assert.Equal((ChannelProvider.Infobip, "447860099299", "+447860099299", "Clinic Name", ChannelIntegrationStatus.Connected), (_row!.Provider, _row.ProviderSenderId, _row.DisplayName, _row.VerifiedName, _row.Status));
        Assert.False(string.IsNullOrEmpty(_row.WebhookVerifyToken));
        Assert.All(new[] { _row.PhoneNumberId, _row.WhatsAppBusinessId, _row.AccessToken, _row.Pin }, Assert.Null);
        Assert.Equal(WhatsAppHealthLevel.Healthy, _row.HealthLevel);
        _cache.Verify(c => c.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(ChannelIntegrationStatus.Connected, r.Status);
    }

    [Fact]
    public async Task Reconnecting_the_same_number_keeps_the_webhook_secret_but_a_new_number_rotates_it()
    {
        await Sut().ConnectAsync(_clinicId, "447860099299");
        var secret = _row!.WebhookVerifyToken;
        await Sut().ConnectAsync(_clinicId, "447860099299");
        Assert.Equal(secret, _row.WebhookVerifyToken);

        _client.Setup(c => c.GetWhatsAppSenderAsync("447860011111", It.IsAny<CancellationToken>())).ReturnsAsync(new InfobipSenderInfo("447860011111", null));
        await Sut().ConnectAsync(_clinicId, "447860011111");
        Assert.NotEqual(secret, _row.WebhookVerifyToken);
    }

    [Fact]
    public async Task Connect_guard_rails()
    {
        _configuration = new ConfigurationBuilder().Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, "447860099299")); // meta provider active

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:Provider"] = "infobip" }).Build();
        _client.SetupGet(c => c.IsConfigured).Returns(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, "447860099299"));

        _client.SetupGet(c => c.IsConfigured).Returns(true);
        _client.Setup(c => c.GetWhatsAppSenderAsync("447860099299", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, "447860099299"));
        Assert.DoesNotContain("down", ex.Message);

        _client.Setup(c => c.GetWhatsAppSenderAsync("447860099299", It.IsAny<CancellationToken>())).ReturnsAsync((InfobipSenderInfo?)null);
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, "447860099299"));
        Assert.Contains("isn't set up", unknown.Message);

        _client.Setup(c => c.GetWhatsAppSenderAsync("447860099299", It.IsAny<CancellationToken>())).ReturnsAsync(new InfobipSenderInfo("447860099299", null));
        _repo.Setup(r => r.IsInfobipSenderConnectedElsewhereAsync("447860099299", _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var taken = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ConnectAsync(_clinicId, "447860099299"));
        Assert.Contains("another clinic", taken.Message);
    }

    [Fact]
    public async Task Webhook_url_is_only_given_for_a_connected_infobip_number()
    {
        Assert.Null(await Sut().GetWebhookUrlAsync(_clinicId));
        await Sut().ConnectAsync(_clinicId, "447860099299");
        var url = await Sut().GetWebhookUrlAsync(_clinicId);
        Assert.StartsWith($"https://app.example.com/api/integrations/whatsapp/connections/{_row!.Id}/events?token=", url);

        _configuration = new ConfigurationBuilder().Build();
        Assert.Null(await Sut().GetWebhookUrlAsync(_clinicId)); // no base url and no request
        _row.Provider = ChannelProvider.Meta;
        Assert.Null(await Sut().GetWebhookUrlAsync(_clinicId));
    }
}
