using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.HttpClients.Calendars;
using PlasticSurgery.Business.HttpClients.N8n;
using PlasticSurgery.Business.HttpClients.TikTok;
using PlasticSurgery.Tests.Support;

namespace PlasticSurgery.Tests.HttpClients;

public class GoogleCalendarProviderClientTests
{
    private readonly FakeHttpHandler _http = new();
    private Dictionary<string, string?> _settings = new() { ["GoogleCalendar:ClientId"] = "cid", ["GoogleCalendar:ClientSecret"] = "csec" };

    private GoogleCalendarProviderClient Sut() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

    [Fact]
    public void Authorization_url_requests_offline_access_and_escapes_values()
    {
        var url = Sut().BuildAuthorizationUrl("https://app/cb?x=1", "st ate");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?client_id=cid", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fapp%2Fcb%3Fx%3D1", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("state=st%20ate", url);
        Assert.Equal(CalendarProvider.Google, Sut().Provider);
    }

    [Fact]
    public async Task Missing_credentials_are_named()
    {
        _settings = new();
        Assert.Contains("ClientId", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ExchangeCodeAsync("c", "r"))).Message);
        _settings = new() { ["GoogleCalendar:ClientId"] = "x" };
        Assert.Contains("ClientSecret", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RefreshAccessTokenAsync("r"))).Message);
    }

    [Fact]
    public async Task Code_exchange_posts_a_form_and_parses_tokens_with_a_default_lifetime()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":1800}");
        var t = await Sut().ExchangeCodeAsync("code", "https://app/cb");
        Assert.Equal(("a", "r"), (t.AccessToken, t.RefreshToken));
        Assert.InRange(t.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(1700), DateTimeOffset.UtcNow.AddSeconds(1900));
        Assert.Contains("grant_type=authorization_code", _http.Requests[0].Body);
        Assert.Contains("code=code", _http.Requests[0].Body);

        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a2\"}");
        var noRefresh = await Sut().ExchangeCodeAsync("code", "https://app/cb");
        Assert.Null(noRefresh.RefreshToken);
        Assert.InRange(noRefresh.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(3500), DateTimeOffset.UtcNow.AddSeconds(3700));
    }

    [Fact]
    public async Task Refresh_keeps_the_old_refresh_token_when_google_sends_none_and_errors_include_the_body()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\"}");
        Assert.Equal("old", (await Sut().RefreshAccessTokenAsync("old")).RefreshToken);
        _http.Enqueue(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RefreshAccessTokenAsync("old"));
        Assert.Contains("invalid_grant", ex.Message);
    }

    [Fact]
    public async Task Account_email_is_null_on_any_failure()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"email\":\"a@b.c\"}");
        Assert.Equal("a@b.c", await Sut().GetAccountEmailAsync("tok"));
        Assert.Equal("Bearer tok", _http.Requests[0].Headers["Authorization"]);
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Null(await Sut().GetAccountEmailAsync("tok"));
        _http.Enqueue(HttpStatusCode.Unauthorized, "{}");
        Assert.Null(await Sut().GetAccountEmailAsync("tok"));
    }

    [Fact]
    public async Task Calendar_list_maps_ids_names_and_primary_flag()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"items\":[{\"id\":\"c1\",\"summary\":\"Work\",\"primary\":true},{\"id\":\"c2\"},{\"id\":\"\"}]}");
        var list = await Sut().ListCalendarsAsync("tok");
        Assert.Equal([("c1", "Work", true), ("c2", "c2", false)], list.Select(c => (c.ExternalCalendarId, c.Name, c.IsPrimary)));
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Empty(await Sut().ListCalendarsAsync("tok"));
        _http.Enqueue(HttpStatusCode.Forbidden, "{\"error\":\"nope\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ListCalendarsAsync("tok"));
    }

    [Fact]
    public async Task Revoke_prefers_the_refresh_token_skips_blanks_and_swallows_failures()
    {
        await Sut().RevokeAsync(null, " ");
        Assert.Empty(_http.Requests);
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await Sut().RevokeAsync("access", "refresh");
        Assert.Contains("token=refresh", _http.Requests[0].Uri.ToString());
        _http.EnqueueThrow(new HttpRequestException("down"));
        await Sut().RevokeAsync("access", null); // must not throw
    }
}

public class OutlookCalendarProviderClientTests
{
    private readonly FakeHttpHandler _http = new();
    private Dictionary<string, string?> _settings = new() { ["MicrosoftCalendar:ClientId"] = "cid", ["MicrosoftCalendar:ClientSecret"] = "csec" };

    private OutlookCalendarProviderClient Sut() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

    [Fact]
    public void Urls_use_the_common_tenant_unless_one_is_configured()
    {
        Assert.StartsWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=cid", Sut().BuildAuthorizationUrl("https://app/cb", "s"));
        _settings["MicrosoftCalendar:TenantId"] = "tenant-1";
        Assert.Contains("/tenant-1/", Sut().BuildAuthorizationUrl("https://app/cb", "s"));
        Assert.Equal(CalendarProvider.Outlook, Sut().Provider);
    }

    [Fact]
    public async Task Token_calls_include_the_scopes_and_keep_the_old_refresh_token()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":600}");
        Assert.Equal("r", (await Sut().ExchangeCodeAsync("c", "https://app/cb")).RefreshToken);
        Assert.Contains("scope=", _http.Requests[0].Body);
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\"}");
        Assert.Equal("old", (await Sut().RefreshAccessTokenAsync("old")).RefreshToken);
        _http.Enqueue(HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RefreshAccessTokenAsync("old"));
    }

    [Fact]
    public async Task Email_prefers_mail_then_principal_name_and_calendars_flag_the_default()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"mail\":\"m@x.com\",\"userPrincipalName\":\"u@x.com\"}");
        Assert.Equal("m@x.com", await Sut().GetAccountEmailAsync("t"));
        _http.Enqueue(HttpStatusCode.OK, "{\"mail\":\"\",\"userPrincipalName\":\"u@x.com\"}");
        Assert.Equal("u@x.com", await Sut().GetAccountEmailAsync("t"));
        _http.Enqueue(HttpStatusCode.Unauthorized, "{}");
        Assert.Null(await Sut().GetAccountEmailAsync("t"));

        _http.Enqueue(HttpStatusCode.OK, "{\"value\":[{\"id\":\"c1\",\"name\":\"Calendar\",\"isDefaultCalendar\":true},{\"id\":\"c2\"}]}");
        Assert.Equal([("c1", "Calendar", true), ("c2", "c2", false)], (await Sut().ListCalendarsAsync("t")).Select(c => (c.ExternalCalendarId, c.Name, c.IsPrimary)));
        _http.Enqueue(HttpStatusCode.Forbidden, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ListCalendarsAsync("t"));
    }

    [Fact]
    public async Task Revoke_is_a_documented_no_op() => await Sut().RevokeAsync("a", "r");
}

public class TikTokProviderClientTests
{
    private readonly FakeHttpHandler _http = new();
    private Dictionary<string, string?> _settings = new() { ["TikTok:ClientKey"] = "ck", ["TikTok:ClientSecret"] = "cs" };

    private TikTokProviderClient Sut() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

    [Fact]
    public async Task Authorization_url_and_missing_configuration()
    {
        Assert.StartsWith("https://www.tiktok.com/v2/auth/authorize/?client_key=ck", Sut().BuildAuthorizationUrl("https://app/cb", "s"));
        _settings = new();
        Assert.Contains("ClientKey", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ExchangeCodeAsync("c", "r"))).Message);
    }

    [Fact]
    public async Task Tokens_are_parsed_with_refresh_expiry_and_defaults()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":100,\"refresh_expires_in\":200}");
        var t = await Sut().ExchangeCodeAsync("c", "https://app/cb");
        Assert.Equal(("a", "r"), (t.AccessToken, t.RefreshToken));
        Assert.NotNull(t.RefreshTokenExpiresAt);

        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"a\"}");
        var refreshed = await Sut().RefreshAccessTokenAsync("old");
        Assert.Equal("old", refreshed.RefreshToken);
        Assert.Null(refreshed.RefreshTokenExpiresAt);
        Assert.InRange(refreshed.ExpiresAt, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
    }

    [Fact]
    public async Task Token_errors_are_detected_even_with_http_200()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"error\":\"invalid_grant\"}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RefreshAccessTokenAsync("r"));
        _http.Enqueue(HttpStatusCode.OK, "{\"foo\":1}");
        Assert.Contains("no access_token", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RefreshAccessTokenAsync("r"))).Message);
        _http.Enqueue(HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ExchangeCodeAsync("c", "r"));
    }

    [Fact]
    public async Task Account_info_parses_the_user_and_surfaces_api_errors()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":{\"user\":{\"open_id\":\"o1\",\"union_id\":\"u1\",\"display_name\":\"Clinic\",\"avatar_url\":\"http://img\"}},\"error\":{\"code\":\"ok\"}}");
        var info = await Sut().GetAccountInfoAsync("tok");
        Assert.Equal(("o1", "u1", "Clinic", "http://img"), (info.OpenId, info.UnionId, info.DisplayName, info.AvatarUrl));

        _http.Enqueue(HttpStatusCode.OK, "{\"error\":{\"code\":\"access_token_invalid\",\"message\":\"expired\"}}");
        Assert.Contains("expired", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetAccountInfoAsync("tok"))).Message);
        _http.Enqueue(HttpStatusCode.Unauthorized, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetAccountInfoAsync("tok"));
    }

    [Fact]
    public async Task Revoke_swallows_failures_and_skips_blank_tokens()
    {
        await Sut().RevokeAsync(null);
        Assert.Empty(_http.Requests);
        _http.EnqueueThrow(new HttpRequestException("down"));
        await Sut().RevokeAsync("tok");
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await Sut().RevokeAsync("tok");
        Assert.Contains("token=tok", _http.Requests[1].Body);
    }
}

public class N8nNotifierTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private Dictionary<string, string?> _settings = new() { ["N8n:AiWebhookUrl"] = "https://n8n.example/webhook/ai", ["N8n:CalendarSyncWebhookUrl"] = "https://n8n.example/webhook/cal", ["N8n:KnowledgeBenchmarkWebhookUrl"] = "https://n8n.example/webhook/bench" };

    private IConfiguration Cfg() => new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();

    private static AiTriggerPayload Payload() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "text", "hi", "btn");

    private void AiAllowed(bool allowed) =>
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed ? ClinicEntitlements.Unrestricted : new ClinicEntitlements(true, true, "active", "basic", "Basic", null, new Dictionary<string, string>()));

    [Fact]
    public async Task Ai_trigger_posts_camel_case_json_when_the_plan_allows_the_ai()
    {
        AiAllowed(true);
        _http.Enqueue(HttpStatusCode.OK, "{}");
        var payload = Payload();
        await new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object).NotifyAsync(payload);
        var req = Assert.Single(_http.Requests);
        Assert.Equal("https://n8n.example/webhook/ai", req.Uri.ToString());
        Assert.Contains($"\"conversationId\":\"{payload.ConversationId}\"", req.Body);
        Assert.Contains("\"selectedValue\":\"btn\"", req.Body);
    }

    [Fact]
    public async Task Ai_trigger_is_skipped_without_a_url_or_without_the_ai_feature_and_never_throws()
    {
        AiAllowed(false);
        var sut = new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object);
        await sut.NotifyAsync(Payload());
        Assert.Empty(_http.Requests);

        _settings.Remove("N8n:AiWebhookUrl");
        await new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object).NotifyAsync(Payload());
        Assert.Empty(_http.Requests);

        _settings["N8n:AiWebhookUrl"] = "https://n8n.example/webhook/ai";
        AiAllowed(true);
        _http.Enqueue(HttpStatusCode.InternalServerError, "oops");
        await new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object).NotifyAsync(Payload());
        _http.EnqueueThrow(new HttpRequestException("down"));
        await new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object).NotifyAsync(Payload());
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await new AiTriggerNotifier(_http.CreateClient(), Cfg(), NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object).NotifyAsync(Payload());
    }

    [Fact]
    public async Task Calendar_sync_notifier_posts_and_tolerates_every_failure()
    {
        var payload = new CalendarSyncTriggerPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "google", "tok", "cal", "create", Guid.NewGuid(), null, DateTimeOffset.UtcNow, null, "UTC", "t", "d", "/l");
        var sut = () => new CalendarSyncNotifier(_http.CreateClient(), Cfg(), NullLogger<CalendarSyncNotifier>.Instance);
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await sut().NotifySyncAsync(payload);
        Assert.Contains("\"operation\":\"create\"", _http.Requests[0].Body);
        _http.Enqueue(HttpStatusCode.BadGateway, "bad");
        await sut().NotifySyncAsync(payload);
        _http.EnqueueThrow(new HttpRequestException("down"));
        await sut().NotifySyncAsync(payload);
        _settings.Remove("N8n:CalendarSyncWebhookUrl");
        await sut().NotifySyncAsync(payload);
        Assert.Equal(3, _http.Requests.Count);
    }

    [Fact]
    public async Task Benchmark_generator_client_sends_chunks_and_parses_an_immediate_reply()
    {
        var sut = () => new N8nKnowledgeBenchmarkGeneratorClient(_http.CreateClient(), Cfg(), NullLogger<N8nKnowledgeBenchmarkGeneratorClient>.Instance);
        Assert.True(sut().IsConfigured);
        var gid = Guid.NewGuid();
        var chunk = new BenchmarkSourceChunk(Guid.NewGuid(), Guid.NewGuid(), "Pricing", "content");
        _http.Enqueue(HttpStatusCode.OK, "{\"generationId\":\"" + gid + "\",\"questions\":[{\"question\":\"q?\"}]}");
        var r = await sut().SendAsync(Guid.NewGuid(), gid, [chunk]);
        Assert.Contains("\"documentTitle\":\"Pricing\"", _http.Requests[0].Body);
        Assert.Single(r.Immediate!.Questions);

        _http.Enqueue(HttpStatusCode.OK, "accepted");
        var async = await sut().SendAsync(Guid.NewGuid(), gid, [chunk]);
        Assert.Null(async.Immediate);
        Assert.Equal("accepted", async.RawBody);
    }

    [Fact]
    public async Task Benchmark_generator_client_failure_modes()
    {
        var sut = () => new N8nKnowledgeBenchmarkGeneratorClient(_http.CreateClient(), Cfg(), NullLogger<N8nKnowledgeBenchmarkGeneratorClient>.Instance);
        _http.Enqueue(HttpStatusCode.InternalServerError, "x");
        Assert.Contains("HTTP 500", (await Assert.ThrowsAsync<BenchmarkGenerationException>(() => sut().SendAsync(Guid.NewGuid(), Guid.NewGuid(), []))).Message);
        _http.EnqueueThrow(new HttpRequestException("down"));
        Assert.Contains("couldn't be reached", (await Assert.ThrowsAsync<BenchmarkGenerationException>(() => sut().SendAsync(Guid.NewGuid(), Guid.NewGuid(), []))).Message);

        _settings.Remove("N8n:KnowledgeBenchmarkWebhookUrl");
        Assert.False(sut().IsConfigured);
        await Assert.ThrowsAsync<BenchmarkGeneratorNotConfiguredException>(() => sut().SendAsync(Guid.NewGuid(), Guid.NewGuid(), []));
    }
}
