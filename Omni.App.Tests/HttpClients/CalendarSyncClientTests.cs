using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.Calendars;
using Omni.App.Business.HttpClients.Calendars;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.Jobs;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.HttpClients;

/// <summary>The calendar sync workflow (docs/n8n/omni-calendar-sync.json) as code: the six provider calls, their bodies, the
/// callback it answers (success or, unlike n8n, failure), and the Calendars / SyncEngine switch.</summary>
public class CalendarSyncClientTests
{
    private readonly FakeHttpHandler _http = new();

    private CalendarSyncClient Sut() => new(_http.CreateClient(), NullLogger<CalendarSyncClient>.Instance);

    private static CalendarSyncTriggerPayload Payload(string provider, string operation, string? externalEventId = "evt-1",
        string? description = "Service: Haircut", DateTimeOffset? end = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), provider, "tok-123", "cal-9", operation, Guid.NewGuid(), externalEventId,
        new DateTimeOffset(2026, 9, 29, 16, 0, 0, TimeSpan.FromHours(3)), end ?? new DateTimeOffset(2026, 9, 29, 16, 30, 0, TimeSpan.FromHours(3)),
        "Asia/Beirut", "Ann — Haircut", description, "/dashboard/bookings/1");

    public static TheoryData<string, string, string, string> Routes => new()
    {
        { CalendarProvider.Google, "create", "POST", "https://www.googleapis.com/calendar/v3/calendars/primary/events" },
        { CalendarProvider.Google, "update", "PATCH", "https://www.googleapis.com/calendar/v3/calendars/primary/events/evt-1" },
        { CalendarProvider.Google, "cancel", "DELETE", "https://www.googleapis.com/calendar/v3/calendars/primary/events/evt-1" },
        { CalendarProvider.Outlook, "create", "POST", "https://graph.microsoft.com/v1.0/me/events" },
        { CalendarProvider.Outlook, "update", "PATCH", "https://graph.microsoft.com/v1.0/me/events/evt-1" },
        { CalendarProvider.Outlook, "cancel", "DELETE", "https://graph.microsoft.com/v1.0/me/events/evt-1" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Each_provider_and_operation_makes_the_workflows_call_and_answers_its_callback(string provider, string operation, string method, string url)
    {
        if (operation == "cancel") _http.Enqueue(HttpStatusCode.NoContent);
        else _http.Enqueue(HttpStatusCode.OK, """{"id":"evt-new","status":"confirmed"}""");
        var payload = Payload(provider, operation);

        var callback = await Sut().SyncAsync(payload);

        var request = Assert.Single(_http.Requests);
        Assert.Equal((method, url), (request.Method.Method, request.Uri.AbsoluteUri));
        Assert.Equal("Bearer tok-123", request.Headers["Authorization"]);
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);
        // $json.id || the payload's externalEventId || null
        var expectedId = operation == "cancel" ? "evt-1" : "evt-new";
        Assert.Equal(new CalendarSyncCallbackRequest(payload.ClientId, payload.BookingId, payload.CalendarIntegrationId, payload.RequestId, true, expectedId, null), callback);
        if (operation == "cancel") Assert.True(string.IsNullOrEmpty(request.Body));
    }

    [Fact]
    public async Task Google_and_outlook_bodies_match_the_workflow()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"id":"g"}""").Enqueue(HttpStatusCode.OK, """{"id":"o"}""");
        await Sut().SyncAsync(Payload(CalendarProvider.Google, "create"));
        await Sut().SyncAsync(Payload(CalendarProvider.Outlook, "update"));

        Assert.Equal("""{"summary":"Ann — Haircut","description":"Service: Haircut\n\n/dashboard/bookings/1","start":{"dateTime":"2026-09-29T16:00:00+03:00","timeZone":"Asia/Beirut"},"end":{"dateTime":"2026-09-29T16:30:00+03:00","timeZone":"Asia/Beirut"}}""",
            JsonNode.Parse(_http.Requests[0].Body!)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var outlook = JsonNode.Parse(_http.Requests[1].Body!)!;
        Assert.Equal(("Ann — Haircut", "text", "Service: Haircut\n\n/dashboard/bookings/1"),
            ((string?)outlook["subject"], (string?)outlook["body"]!["contentType"], (string?)outlook["body"]!["content"]));
        Assert.Equal(("2026-09-29T16:30:00+03:00", "Asia/Beirut"), ((string?)outlook["end"]!["dateTime"], (string?)outlook["end"]!["timeZone"]));
        Assert.Null(outlook["summary"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_description_is_left_out_of_the_join(string? description)
    {
        _http.Enqueue(HttpStatusCode.OK, "{}");
        var payload = Payload(CalendarProvider.Google, "update", description: description);

        var callback = await Sut().SyncAsync(payload);

        Assert.Equal("/dashboard/bookings/1", (string?)JsonNode.Parse(_http.Requests[0].Body!)!["description"]);
        Assert.Equal("evt-1", callback!.ExternalEventId); // no id in the answer: the payload's
    }

    [Fact]
    public async Task A_created_event_without_any_id_reports_none()
    {
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.Null((await Sut().SyncAsync(Payload(CalendarProvider.Outlook, "create", externalEventId: null)))!.ExternalEventId);
    }

    [Fact]
    public async Task Provider_failures_are_reported_unlike_n8n()
    {
        _http.Enqueue(HttpStatusCode.NotFound, """{"error":{"message":"Not Found"}}""");
        var failed = await Sut().SyncAsync(Payload(CalendarProvider.Google, "update"));
        Assert.False(failed!.Success);
        Assert.Equal("Google Calendar answered HTTP 404 to the update.", failed.ErrorMessage);
        Assert.Null(failed.ExternalEventId);

        _http.EnqueueThrow(new HttpRequestException("dns"));
        var unreachable = await Sut().SyncAsync(Payload(CalendarProvider.Outlook, "create"));
        Assert.Equal((false, "Outlook calendar couldn't be reached for the create."), (unreachable!.Success, unreachable.ErrorMessage));
    }

    [Theory]
    [InlineData("apple", "create")]
    [InlineData(CalendarProvider.Google, "move")]
    public async Task An_unknown_provider_or_operation_calls_nothing_and_answers_nothing(string provider, string operation)
    {
        Assert.Null(await Sut().SyncAsync(Payload(provider, operation)));
        Assert.Empty(_http.Requests);
    }

    // ---------------------------------------------------------------- Calendars / SyncEngine

    private (CalendarSyncDispatcher Sut, CalendarSyncQueue Queue) Dispatcher(string engine)
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.CalendarsSyncEngine).Returns(engine);
        var n8n = new CalendarSyncNotifier(_http.CreateClient(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:CalendarSyncWebhookUrl"] = "https://n8n.example/webhook/cal" }).Build(),
            NullLogger<CalendarSyncNotifier>.Instance);
        var queue = new CalendarSyncQueue();
        return (new CalendarSyncDispatcher(config.Object, n8n, queue, NullLogger<CalendarSyncDispatcher>.Instance), queue);
    }

    [Fact]
    public async Task On_n8n_the_sync_is_posted_to_the_workflow_as_before()
    {
        var (sut, queue) = Dispatcher(WorkflowEngines.N8n);
        _http.Enqueue(HttpStatusCode.OK, "{}");

        await sut.NotifySyncAsync(Payload(CalendarProvider.Google, "create"));

        Assert.Equal("https://n8n.example/webhook/cal", Assert.Single(_http.Requests).Uri.ToString());
        Assert.Contains("\"operation\":\"create\"", _http.Requests[0].Body);
        queue.Complete();
        await Assert.ThrowsAnyAsync<Exception>(async () => await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task On_code_the_sync_is_queued_and_the_worker_makes_the_call_then_applies_the_callback()
    {
        var (sut, queue) = Dispatcher(WorkflowEngines.Code);
        var payload = Payload(CalendarProvider.Google, "create");
        await sut.NotifySyncAsync(payload);
        Assert.Empty(_http.Requests);

        var applied = new TaskCompletionSource<CalendarSyncCallbackRequest>();
        var calendars = new Mock<ICalendarIntegrationService>();
        calendars.Setup(c => c.ApplySyncCallbackAsync(It.IsAny<CalendarSyncCallbackRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CalendarSyncCallbackRequest r, CancellationToken _) => applied.TrySetResult(r)).Returns(Task.CompletedTask);
        _http.Enqueue(HttpStatusCode.OK, """{"id":"evt-new"}""");
        var provider = new ServiceCollection()
            .AddSingleton<ICalendarSyncClient>(Sut())
            .AddSingleton(calendars.Object)
            .BuildServiceProvider();
        var worker = new CalendarSyncWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CalendarSyncWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);

        var callback = await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((payload.RequestId, true, "evt-new"), (callback.RequestId, callback.Success, callback.ExternalEventId));
        Assert.Equal("https://www.googleapis.com/calendar/v3/calendars/primary/events", Assert.Single(_http.Requests).Uri.AbsoluteUri);

        await worker.StopAsync(CancellationToken.None);
        await sut.NotifySyncAsync(payload); // a stopping app drops it quietly
    }
}
