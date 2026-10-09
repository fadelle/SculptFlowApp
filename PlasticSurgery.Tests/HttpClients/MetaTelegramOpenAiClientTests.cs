using System.Net;
using Microsoft.Extensions.Configuration;
using PlasticSurgery.Business.HttpClients.Meta;
using PlasticSurgery.Business.HttpClients.OpenAi;
using PlasticSurgery.Business.HttpClients.Telegram;
using PlasticSurgery.Tests.Support;

namespace PlasticSurgery.Tests.HttpClients;

public class MetaGraphClientTests
{
    private readonly FakeHttpHandler _http = new();
    private Dictionary<string, string?> _settings = new() { ["Meta:AppId"] = "app 1", ["Meta:AppSecret"] = "sec&ret" };

    private MetaGraphClient Sut() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

    [Fact]
    public async Task Missing_app_configuration_is_reported_clearly()
    {
        _settings = new();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ExchangeCodeForTokenAsync("c"));
        Assert.Contains("Meta:AppId", ex.Message);
        _settings = new() { ["Meta:AppId"] = "1" };
        Assert.Contains("Meta:AppSecret", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ExchangeCodeForTokenAsync("c"))).Message);
    }

    [Fact]
    public async Task Code_exchange_escapes_parameters_uses_the_default_version_and_optional_redirect()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
        Assert.Equal("tok", await Sut().ExchangeCodeForTokenAsync("a/b", "https://r?x=1"));
        var url = _http.Requests[0].Uri.AbsoluteUri;
        Assert.StartsWith("https://graph.facebook.com/v21.0/oauth/access_token?", url);
        Assert.Contains("client_id=app%201", url);
        Assert.Contains("client_secret=sec%26ret", url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fr%3Fx%3D1", url);

        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"tok2\"}");
        await Sut().ExchangeCodeForTokenAsync("c");
        Assert.DoesNotContain("redirect_uri", _http.Requests[1].Uri.AbsoluteUri);
    }

    [Fact]
    public async Task The_graph_version_is_configurable_and_errors_include_the_status_and_body()
    {
        _settings["Meta:GraphApiVersion"] = "v99.0";
        _http.Enqueue(HttpStatusCode.BadRequest, "{\"error\":\"bad code\"}");
        var ex = await Assert.ThrowsAsync<MetaGraphApiException>(() => Sut().ExchangeCodeForTokenAsync("c"));
        Assert.Contains("v99.0", _http.Requests[0].Uri.AbsoluteUri);
        Assert.Contains("400", ex.Message);
        Assert.Contains("bad code", ex.Message);
    }

    [Fact]
    public async Task Long_lived_token_and_managed_page()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"long\"}");
        Assert.Equal("long", await Sut().GetLongLivedTokenAsync("short"));
        Assert.Contains("grant_type=fb_exchange_token", _http.Requests[0].Uri.AbsoluteUri);

        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"p1\",\"name\":\"Page\",\"access_token\":\"pt\"},{\"id\":\"p2\",\"name\":\"Other\",\"access_token\":\"x\"}]}");
        var page = await Sut().GetFirstManagedPageAsync("u");
        Assert.Equal(("p1", "Page", "pt"), (page!.PageId, page.PageName, page.PageAccessToken));

        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[]}");
        Assert.Null(await Sut().GetFirstManagedPageAsync("u"));
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Null(await Sut().GetFirstManagedPageAsync("u"));
    }

    [Fact]
    public async Task Phone_number_details()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"display_phone_number\":\"+961 70\",\"verified_name\":\"Clinic\"}");
        var info = await Sut().GetWhatsAppPhoneNumberAsync("P1", "tok");
        Assert.Equal(("+961 70", "Clinic"), (info!.DisplayPhoneNumber, info.VerifiedName));
        _http.Enqueue(HttpStatusCode.OK, "{\"id\":\"P1\"}");
        Assert.Null(await Sut().GetWhatsAppPhoneNumberAsync("P1", "tok"));
    }

    [Fact]
    public async Task Granted_scopes_use_the_app_token_for_debug_token()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":{\"scopes\":[\"whatsapp_business_management\",null,\"business_management\"]}}");
        Assert.Equal(["whatsapp_business_management", "business_management"], await Sut().GetGrantedScopesAsync("tok"));
        Assert.Contains("access_token=app%201%7Csec%26ret", _http.Requests[0].Uri.AbsoluteUri);
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":{}}");
        Assert.Empty(await Sut().GetGrantedScopesAsync("tok"));
    }

    [Fact]
    public async Task Waba_discovery_walks_owned_and_client_edges_and_skips_forbidden_ones()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"b1\",\"name\":\"Biz\"},{\"id\":\"b2\"}]}");
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"w1\",\"name\":\"Owned WABA\"}]}");       // b1 owned
        _http.Enqueue(HttpStatusCode.Forbidden, "{}");                                                  // b1 client → skipped
        _http.Enqueue(HttpStatusCode.OK, "{}");                                                         // b2 owned: no data
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"w2\"}]}");                               // b2 client
        var wabas = await Sut().GetClientWhatsAppBusinessAccountsAsync("tok");
        Assert.Equal([("b1", "Biz", "w1", "Owned WABA", "owned"), ("b2", "b2", "w2", "w2", "client")],
            wabas.Select(w => (w.BusinessId, w.BusinessName, w.WabaId, w.WabaName, w.Relationship)));

        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Empty(await Sut().GetClientWhatsAppBusinessAccountsAsync("tok"));
    }

    [Fact]
    public async Task Waba_phone_numbers()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[{\"id\":\"P1\",\"display_phone_number\":\"+1\",\"verified_name\":\"V\"},{\"id\":\"P2\"}]}");
        var phones = await Sut().GetPhoneNumbersForWabaAsync("W1", "tok");
        Assert.Equal([("P1", "+1", "V"), ("P2", "P2", null)], phones.Select(p => (p.PhoneNumberId, p.DisplayPhoneNumber, p.VerifiedName)));
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Empty(await Sut().GetPhoneNumbersForWabaAsync("W1", "tok"));
    }

    [Fact]
    public async Task Registering_a_number_posts_the_pin_and_failures_surface()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"success\":true}");
        await Sut().RegisterPhoneNumberAsync("P1", "tok", "123456");
        Assert.Equal(HttpMethod.Post, _http.Requests[0].Method);
        Assert.Contains("\"pin\":\"123456\"", _http.Requests[0].Body);
        Assert.Contains("whatsapp", _http.Requests[0].Body);
        _http.Enqueue(HttpStatusCode.BadRequest, "{}");
        await Assert.ThrowsAsync<MetaGraphApiException>(() => Sut().RegisterPhoneNumberAsync("P1", "tok", "123456"));
    }

    [Fact]
    public async Task Template_creation_and_status()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"id\":\"t1\",\"status\":\"APPROVED\"}");
        var created = await Sut().CreateMessageTemplateAsync("W1", "tok", "promo", "marketing", "en", new[] { new { type = "BODY" } });
        Assert.Equal(("t1", "APPROVED"), (created.Id, created.Status));
        Assert.Contains("\"category\":\"MARKETING\"", _http.Requests[0].Body);

        _http.Enqueue(HttpStatusCode.OK, "{\"id\":\"t2\"}");
        Assert.Equal("PENDING", (await Sut().CreateMessageTemplateAsync("W1", "tok", "p", "utility", "en", new { })).Status);

        _http.Enqueue(HttpStatusCode.OK, "{\"status\":\"REJECTED\",\"rejected_reason\":\"INVALID_FORMAT\"}");
        var rejected = await Sut().GetMessageTemplateStatusAsync("t1", "tok");
        Assert.Equal(("REJECTED", "INVALID_FORMAT"), (rejected.Status, rejected.RejectedReason));
        _http.Enqueue(HttpStatusCode.OK, "{\"status\":\"APPROVED\",\"rejected_reason\":\"NONE\"}");
        Assert.Null((await Sut().GetMessageTemplateStatusAsync("t1", "tok")).RejectedReason);
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Equal("PENDING", (await Sut().GetMessageTemplateStatusAsync("t1", "tok")).Status);
    }
}

public class TelegramBotClientTests
{
    private readonly FakeHttpHandler _http = new();
    private string? _baseUrl;

    private TelegramBotClient Sut() => new(_http.CreateClient(),
        new ConfigurationBuilder().AddInMemoryCollection(_baseUrl is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["Telegram:ApiBaseUrl"] = _baseUrl }).Build());

    private static string Ok(string result) => "{\"ok\":true,\"result\":" + result + "}";

    [Fact]
    public async Task GetMe_calls_the_bot_endpoint_with_the_token_in_the_path()
    {
        _http.Enqueue(HttpStatusCode.OK, Ok("{\"id\":555,\"username\":\"clinic_bot\",\"first_name\":\"Clinic\"}"));
        var me = await Sut().GetMeAsync("123:AAA");
        Assert.Equal("https://api.telegram.org/bot123:AAA/getMe", _http.Requests[0].Uri.ToString());
        Assert.Equal((555L, "clinic_bot", "Clinic"), (me.Id, me.Username, me.FirstName));
    }

    [Fact]
    public async Task A_custom_api_base_url_is_honoured_and_trimmed()
    {
        _baseUrl = "https://tg.proxy.local/";
        _http.Enqueue(HttpStatusCode.OK, Ok("{\"id\":1}"));
        var me = await Sut().GetMeAsync("t");
        Assert.StartsWith("https://tg.proxy.local/bot", _http.Requests[0].Uri.ToString());
        Assert.Null(me.Username);
    }

    [Fact]
    public async Task SetWebhook_sends_the_secret_and_only_asks_for_messages_and_delete_keeps_pending_updates()
    {
        _http.Enqueue(HttpStatusCode.OK, Ok("true")).Enqueue(HttpStatusCode.OK, Ok("true"));
        await Sut().SetWebhookAsync("t", "https://x/hook", "s3cret");
        Assert.Contains("\"secret_token\":\"s3cret\"", _http.Requests[0].Body);
        Assert.Contains("\"allowed_updates\":[\"message\"]", _http.Requests[0].Body);
        await Sut().DeleteWebhookAsync("t");
        Assert.Contains("\"drop_pending_updates\":false", _http.Requests[1].Body);
    }

    [Fact]
    public async Task Webhook_info_parses_optional_fields()
    {
        _http.Enqueue(HttpStatusCode.OK, Ok("{\"url\":\"https://x\",\"pending_update_count\":3,\"last_error_message\":\"timeout\",\"last_error_date\":1700000000}"));
        var info = await Sut().GetWebhookInfoAsync("t");
        Assert.Equal(("https://x", 3, "timeout", DateTimeOffset.FromUnixTimeSeconds(1700000000)), (info.Url, info.PendingUpdateCount, info.LastErrorMessage, info.LastErrorDate));
        _http.Enqueue(HttpStatusCode.OK, Ok("{}"));
        var empty = await Sut().GetWebhookInfoAsync("t");
        Assert.Equal((null, 0, null, null), (empty.Url, empty.PendingUpdateCount, empty.LastErrorMessage, empty.LastErrorDate));
    }

    [Fact]
    public async Task SendMessage_uses_numeric_chat_ids_when_possible_and_enforces_the_length_limit()
    {
        _http.Enqueue(HttpStatusCode.OK, Ok("{\"message_id\":77}")).Enqueue(HttpStatusCode.OK, Ok("{\"message_id\":78}"));
        Assert.Equal(77, await Sut().SendMessageAsync("t", "42", "hi"));
        Assert.Contains("\"chat_id\":42", _http.Requests[0].Body);
        await Sut().SendMessageAsync("t", "@channel", "hi");
        Assert.Contains("\"chat_id\":\"@channel\"", _http.Requests[1].Body);
        await Assert.ThrowsAsync<TelegramApiException>(() => Sut().SendMessageAsync("t", "42", new string('x', TelegramBotClient.MaxMessageLength + 1)));
        Assert.Equal(2, _http.Requests.Count);
    }

    [Theory]
    [InlineData(401, "Unauthorized", "rejected the bot token")]
    [InlineData(403, "Forbidden", "blocked the bot")]
    [InlineData(429, "Too Many Requests", "rate-limiting")]
    [InlineData(400, "Bad Request: chat not found", "Telegram error 400: Bad Request: chat not found")]
    public async Task Telegram_errors_get_friendly_messages(int code, string description, string expected)
    {
        _http.Enqueue((HttpStatusCode)code, "{\"ok\":false,\"error_code\":" + code + ",\"description\":\"" + description + "\"}");
        var ex = await Assert.ThrowsAsync<TelegramApiException>(() => Sut().GetMeAsync("t"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Blank_tokens_unreachable_hosts_and_garbage_responses_are_handled()
    {
        await Assert.ThrowsAsync<TelegramApiException>(() => Sut().GetMeAsync(" "));
        Assert.Empty(_http.Requests);
        _http.EnqueueThrow(new HttpRequestException("dns"));
        var unreachable = await Assert.ThrowsAsync<TelegramApiException>(() => Sut().GetMeAsync("t"));
        Assert.Contains("Could not reach", unreachable.Message);
        Assert.DoesNotContain("t/", unreachable.Message);
        _http.Enqueue(HttpStatusCode.BadGateway, "<html>bad gateway</html>");
        var garbage = await Assert.ThrowsAsync<TelegramApiException>(() => Sut().GetMeAsync("t"));
        Assert.Contains("unreadable", garbage.Message);
    }
}

public class OpenAiEmbeddingServiceTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IConfigManager> _config = new();

    public OpenAiEmbeddingServiceTests()
    {
        _config.SetupGet(c => c.EmbeddingsApiKey).Returns("sk-test");
        _config.SetupGet(c => c.EmbeddingsBaseUrl).Returns("https://api.openai.example/v1/");
        _config.SetupGet(c => c.EmbeddingsModel).Returns("text-embedding-3-small");
        _config.SetupGet(c => c.EmbeddingsDimensions).Returns(3);
        _config.SetupGet(c => c.EmbeddingsMaxInputsPerRequest).Returns(2);
    }

    private OpenAiEmbeddingService Sut() => new(_http.CreateClient(), new ConfigurationBuilder().Build(), _config.Object);

    private static string Reply(params (int Index, float[] Vector)[] items) =>
        "{\"data\":[" + string.Join(",", items.Select(i => $"{{\"index\":{i.Index},\"embedding\":[{string.Join(",", i.Vector.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]}}")) + "]}";

    [Fact]
    public void Dimensions_come_from_configuration() => Assert.Equal(3, Sut().Dimensions);

    [Fact]
    public async Task A_missing_api_key_fails_before_any_request()
    {
        _config.SetupGet(c => c.EmbeddingsApiKey).Returns(" ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("hi"));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Single_embedding_sends_model_dimensions_and_bearer_key()
    {
        _http.Enqueue(HttpStatusCode.OK, Reply((0, [0.1f, 0.2f, 0.3f])));
        var v = await Sut().EmbedAsync("hello");
        Assert.Equal([0.1f, 0.2f, 0.3f], v);
        var req = _http.Requests[0];
        Assert.Equal("https://api.openai.example/v1/embeddings", req.Uri.ToString());
        Assert.Equal("Bearer sk-test", req.Headers["Authorization"]);
        Assert.Contains("\"dimensions\":3", req.Body);
        Assert.Contains("\"model\":\"text-embedding-3-small\"", req.Body);
    }

    [Fact]
    public async Task Batches_are_split_by_the_configured_size_and_results_keep_input_order_even_if_returned_shuffled()
    {
        _http.Enqueue(HttpStatusCode.OK, Reply((1, [4f, 4f, 4f]), (0, [3f, 3f, 3f])));
        _http.Enqueue(HttpStatusCode.OK, Reply((0, [5f, 5f, 5f])));
        var vectors = await Sut().EmbedBatchAsync(["a", "b", "c"]);
        Assert.Equal(2, _http.Requests.Count);
        Assert.Equal([3f, 4f, 5f], vectors.Select(v => v[0]));
    }

    [Fact]
    public async Task Non_v3_models_omit_the_dimensions_parameter_and_explicit_overrides_win()
    {
        _http.Enqueue(HttpStatusCode.OK, Reply((0, [1f, 1f])));
        await Sut().EmbedAsync("x", model: "text-embedding-ada-002", dimensions: 2);
        Assert.DoesNotContain("dimensions", _http.Requests[0].Body);
        Assert.Contains("ada-002", _http.Requests[0].Body);
    }

    [Fact]
    public async Task Wrong_dimensions_missing_vectors_and_bad_json_are_reported()
    {
        _http.Enqueue(HttpStatusCode.OK, Reply((0, [1f, 2f])));
        var dim = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
        Assert.Contains("returned 2 dimensions", dim.Message);

        _http.Enqueue(HttpStatusCode.OK, "{\"data\":[]}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
        _http.Enqueue(HttpStatusCode.OK, "not json");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
        _http.Enqueue(HttpStatusCode.OK, Reply((5, [1f, 1f, 1f])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
    }

    [Fact]
    public async Task Transport_problems_and_http_errors_are_wrapped()
    {
        _http.EnqueueThrow(new HttpRequestException("dns"));
        Assert.Contains("unreachable", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"))).Message);
        _http.EnqueueThrow(new TaskCanceledException("t"));
        Assert.Contains("timed out", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"))).Message);
        _http.Enqueue(HttpStatusCode.InternalServerError, new string('e', 500));
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
        Assert.Contains("HTTP 500", failed.Message);
        Assert.True(failed.Message.Length < 400);
    }

    [Fact]
    public async Task Provider_error_bodies_never_reach_the_caller()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Incorrect API key provided: sk-test***abcd\"}}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().EmbedAsync("x"));
        Assert.DoesNotContain("sk-test", ex.Message);
    }
}
