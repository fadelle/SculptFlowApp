using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.HttpClients.Infobip;
using PlasticSurgery.Tests.Support;

namespace PlasticSurgery.Tests.HttpClients;

public class InfobipClientTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IConfigManager> _config = new();
    private Dictionary<string, string?> _settings = new() { ["Infobip:BaseUrl"] = "api.infobip.example/", ["Infobip:ApiKey"] = " key123 " };

    public InfobipClientTests() => _config.SetupGet(c => c.InfobipMaxSendAttempts).Returns(2);

    private InfobipClient Sut() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build(), NullLogger<InfobipClient>.Instance, _config.Object);

    private static InfobipWhatsAppTextMessage Text(string id = "m1") => new("447000", "96170123456", id, new InfobipTextContent("hello"));

    private static InfobipWhatsAppTemplateMessage Template(string id = "t1") =>
        new("447000", "96170123456", id, new InfobipTemplateContent("promo", new InfobipTemplateData(new InfobipTemplateBody(["Ann"])), "en"));

    [Theory]
    [InlineData("api.x", "key", true)]
    [InlineData("https://api.x", "key", true)]
    [InlineData("api.x", "", false)]
    [InlineData("", "key", false)]
    public void Configured_needs_both_values(string url, string key, bool expected)
    {
        _settings = new() { ["Infobip:BaseUrl"] = url, ["Infobip:ApiKey"] = key };
        Assert.Equal(expected, Sut().IsConfigured);
    }

    [Fact]
    public async Task Unconfigured_client_refuses_to_send()
    {
        _settings = new();
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.Contains("isn't configured", ex.Message);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Text_send_posts_json_with_the_app_authorization_header_and_https_added()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"messageId\":\"srv-1\",\"status\":{\"groupName\":\"PENDING\",\"name\":\"PENDING_ENROUTE\"}}");
        var r = await Sut().SendWhatsAppTextAsync(Text());
        var req = Assert.Single(_http.Requests);
        Assert.Equal("https://api.infobip.example/whatsapp/1/message/text", req.Uri.ToString());
        Assert.Equal("App key123", req.Headers["Authorization"]);
        Assert.Contains("\"messageId\":\"m1\"", req.Body);
        Assert.DoesNotContain("notifyUrl", req.Body); // nulls are omitted
        Assert.Equal(("srv-1", "PENDING", "PENDING_ENROUTE"), (r.MessageId, r.StatusGroup, r.StatusName));
    }

    [Fact]
    public async Task Template_send_wraps_the_message_and_reads_the_first_result()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"messages\":[{\"messageId\":\"srv-t\",\"status\":{\"groupName\":\"PENDING\"}}]}");
        var r = await Sut().SendWhatsAppTemplateAsync(Template());
        Assert.Contains("\"messages\":[{", _http.Requests[0].Body);
        Assert.Equal("srv-t", r.MessageId);

        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Equal("t2", (await Sut().SendWhatsAppTemplateAsync(Template("t2"))).MessageId); // falls back to our id
    }

    [Fact]
    public async Task A_rejected_status_group_becomes_an_exception()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"status\":{\"groupName\":\"REJECTED\",\"name\":\"REJECTED_NOT_ENOUGH_CREDITS\"}}");
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.True(ex.IsRejected);
        Assert.Equal("REJECTED_NOT_ENOUGH_CREDITS", ex.ErrorId);
    }

    [Fact]
    public async Task Rate_limits_and_503_are_retried_once_then_succeed()
    {
        _http.Enqueue(HttpStatusCode.TooManyRequests, "{}").Enqueue(HttpStatusCode.OK, "{\"messageId\":\"srv\"}");
        var r = await Sut().SendWhatsAppTextAsync(Text());
        Assert.Equal(2, _http.Requests.Count);
        Assert.Equal("srv", r.MessageId);
    }

    [Fact]
    public async Task Persistent_503_gives_up_after_the_configured_attempts_with_the_parsed_error()
    {
        _http.Enqueue(HttpStatusCode.ServiceUnavailable, "{}")
             .Enqueue(HttpStatusCode.ServiceUnavailable, "{\"requestError\":{\"serviceException\":{\"messageId\":\"SVC\",\"text\":\"busy\",\"validationErrors\":{\"x\":[\"bad\"]}}}}");
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.Equal((503, "SVC"), (ex.StatusCode, ex.ErrorId));
        Assert.Contains("busy", ex.Message);
        Assert.Contains("\"x\"", ex.Detail);
        Assert.False(ex.IsRejected);
    }

    [Fact]
    public async Task Client_errors_are_not_retried_and_non_json_bodies_are_tolerated()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, "<html>nope</html>");
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.Single(_http.Requests);
        Assert.True(ex.IsRejected);
        Assert.Null(ex.ErrorId);
    }

    [Fact]
    public async Task Connection_failures_are_retried_then_reported_as_unreachable()
    {
        _http.EnqueueThrow(new HttpRequestException("dns")).EnqueueThrow(new HttpRequestException("dns"));
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.Equal(2, _http.Requests.Count);
        Assert.Contains("Couldn't reach", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task A_timeout_is_flagged_and_never_retried_because_the_send_may_have_happened()
    {
        _http.EnqueueThrow(new TaskCanceledException("timeout"));
        var ex = await Assert.ThrowsAsync<InfobipApiException>(() => Sut().SendWhatsAppTextAsync(Text()));
        Assert.True(ex.IsTimeout);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Sender_lookup_returns_the_display_name_or_null_for_unknown_senders()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"displayName\":\"Clinic\"}");
        var info = await Sut().GetWhatsAppSenderAsync("+44 786");
        Assert.Equal("Clinic", info!.DisplayName);
        Assert.Contains("senders/%2B44%2078", _http.Requests[0].Uri.AbsoluteUri.Replace("%20", "%20"));

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden })
        {
            _http.Enqueue(status, "{}");
            Assert.Null(await Sut().GetWhatsAppSenderAsync("447"));
        }

        _http.Enqueue(HttpStatusCode.OK, "{\"displayName\":5}");
        Assert.Null((await Sut().GetWhatsAppSenderAsync("447"))!.DisplayName);

        _http.Enqueue(HttpStatusCode.InternalServerError, "{}");
        await Assert.ThrowsAsync<InfobipApiException>(() => Sut().GetWhatsAppSenderAsync("447"));
    }

    [Fact]
    public async Task Templates_can_be_created_and_read_with_numeric_or_string_ids()
    {
        _http.Enqueue(HttpStatusCode.OK, "{\"id\":12345,\"status\":\"APPROVED\"}");
        var created = await Sut().CreateWhatsAppTemplateAsync("447000", new { name = "promo" });
        Assert.Equal(("12345", "APPROVED"), (created.Id, created.Status));
        Assert.Contains("/whatsapp/2/senders/447000/templates", _http.Requests[0].Uri.ToString());

        _http.Enqueue(HttpStatusCode.OK, "{\"id\":\"abc\"}");
        var read = await Sut().GetWhatsAppTemplateAsync("447000", "abc");
        Assert.Equal(("abc", "PENDING"), (read.Id, read.Status));

        _http.Enqueue(HttpStatusCode.OK, "{}");
        await Assert.ThrowsAsync<InfobipApiException>(() => Sut().GetWhatsAppTemplateAsync("447000", "x"));
        _http.Enqueue(HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<InfobipApiException>(() => Sut().GetWhatsAppTemplateAsync("447000", "x"));
    }
}
