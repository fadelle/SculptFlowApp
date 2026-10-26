using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.Ai;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.Jobs;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.Engines;

/// <summary>BotTrigger: Ai / BotEngine = n8n hands the trigger to AiTriggerNotifier unchanged; code keeps every check and trace
/// step of the n8n trigger but queues the run for BotEngine.</summary>
public class BotTriggerTests
{
    private readonly BotRunQueue _queue = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IAiProfileService> _profiles = AiProfileKit.Profiles();
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly FakeHttpHandler _http = new();
    private Mock<IInboxSettingsService> _inbox = BotKit.Inbox();

    public BotTriggerTests()
    {
        AiAllowed(true);
        _config.SetupGet(c => c.AiBotEngine).Returns(WorkflowEngines.Code);
    }

    private BotTrigger Sut()
    {
        var n8n = new AiTriggerNotifier(_http.CreateClient(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:AiWebhookUrl"] = "https://n8n.example/webhook/ai" }).Build(),
            NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object, _profiles.Object, _inbox.Object, BotKit.Runtime().Object, _tracer.Object);
        return new(NullLogger<BotTrigger>.Instance, _entitlements.Object, _profiles.Object, _inbox.Object, BotKit.Runtime().Object,
            _queue, _tracer.Object, _config.Object, n8n);
    }

    private static AiTriggerPayload Payload() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "text", "hi", "btn");

    private void AiAllowed(bool allowed) =>
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allowed ? ClientEntitlements.Unrestricted : new ClientEntitlements(true, true, "active", "basic", "Basic", null, new Dictionary<string, string>()));

    private List<AiTriggerPayload> Queued()
    {
        var runs = new List<AiTriggerPayload>();
        while (_queue.TryDequeue(out var run)) runs.Add(run);
        return runs;
    }

    private void Traced(string step, string outcome, string? detail = null) =>
        _tracer.Verify(t => t.Step(step, outcome, It.IsAny<Omni.App.Entities.Dtos.Monitoring.TraceIds>(), It.IsAny<double?>(),
            It.Is<object?>(d => detail == null || System.Text.Json.JsonSerializer.Serialize(d, (System.Text.Json.JsonSerializerOptions?)null).Contains(detail))), Times.Once);

    [Fact]
    public async Task A_contact_message_is_queued_with_the_bot_and_the_clients_profile()
    {
        var payload = Payload();
        _profiles.Setup(p => p.GetContactLanguageAsync(payload.ClientId, payload.ContactId!.Value, It.IsAny<CancellationToken>())).ReturnsAsync("fr");

        await Sut().NotifyAsync(payload);

        var run = Assert.Single(Queued());
        Assert.Equal((payload.ConversationId, "hi", "btn"), (run.ConversationId, run.MessageText, run.SelectedValue));
        Assert.Equal(("Cedar Dental", "dental", "ar", "Asia/Beirut", "fr"), (run.ClientName, run.BusinessType, run.Language, run.Timezone, run.ContactLanguage));
        Assert.Equal(AiProfileKit.Sample.AiProfile, run.AiProfile);
        Assert.Equal("visits", run.Terms!["booking"].Other);
        Assert.Equal(BotKit.Config, run.Bot);
        _inbox.Verify(i => i.ResolveAsync(payload.ClientId, "whatsapp", It.IsAny<CancellationToken>()), Times.Once);
        Traced(TraceSteps.BotTriggered, TraceOutcomes.Ok, "\"engine\":\"code\",\"queued\":true");
        Assert.Empty(_http.Requests);
    }

    [Theory]
    [InlineData(WorkflowEngines.N8n)]
    [InlineData("")]
    public async Task On_n8n_the_message_goes_to_the_workflow_as_before_and_nothing_is_queued(string engine)
    {
        _config.SetupGet(c => c.AiBotEngine).Returns(engine);
        var payload = Payload();
        _http.Enqueue(HttpStatusCode.OK, "{}");

        await Sut().NotifyAsync(payload);

        var request = Assert.Single(_http.Requests);
        Assert.Equal("https://n8n.example/webhook/ai", request.Uri.ToString());
        Assert.Contains($"\"conversationId\":\"{payload.ConversationId}\"", request.Body);
        Assert.Contains("\"clientName\":\"Cedar Dental\"", request.Body);
        Assert.Empty(Queued());
        Traced(TraceSteps.BotTriggered, TraceOutcomes.Ok, "\"engine\":\"n8n\"");

        // The test panel follows the same switch.
        var session = Guid.NewGuid();
        _http.Enqueue(HttpStatusCode.OK, "{}");
        Assert.True(await Sut().NotifyTestAsync(new AiTriggerPayload(Guid.NewGuid(), session, session, Guid.NewGuid(), "test", "text", "hi", Bot: BotKit.Config, TestSessionId: session)));
        Assert.Contains($"\"testSessionId\":\"{session}\"", _http.Requests[1].Body);
        _http.Enqueue(HttpStatusCode.BadGateway, "down");
        Assert.False(await Sut().NotifyTestAsync(new AiTriggerPayload(Guid.NewGuid(), session, session, Guid.NewGuid(), "test", "text", "hi", Bot: BotKit.Config, TestSessionId: session)));
        Assert.Empty(Queued());
    }

    [Fact]
    public async Task No_ai_assistant_no_bot_or_no_client_skips_like_before()
    {
        AiAllowed(false);
        await Sut().NotifyAsync(Payload());
        Traced(TraceSteps.BotSkipped, TraceOutcomes.Skipped, "plan_without_ai_assistant");

        AiAllowed(true);
        _inbox = BotKit.Inbox(AiBotBlockReason.BotInactive);
        await Sut().NotifyAsync(Payload());

        _inbox = BotKit.Inbox();
        _profiles.Setup(p => p.GetForClientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((AiClientProfile?)null);
        await Sut().NotifyAsync(Payload());
        Traced(TraceSteps.BotSkipped, TraceOutcomes.Skipped, "client_not_found");

        Assert.Empty(Queued());
    }

    [Fact]
    public async Task A_test_message_is_queued_as_it_is_and_says_whether_it_was()
    {
        var session = Guid.NewGuid();
        var payload = new AiTriggerPayload(Guid.NewGuid(), session, session, Guid.NewGuid(), "test", "text", "hi", Bot: BotKit.Config, TestSessionId: session);

        Assert.True(await Sut().NotifyTestAsync(payload));
        var run = Assert.Single(Queued());
        Assert.Equal((session, (string?)null, "Cedar Dental"), (run.TestSessionId, run.ContactLanguage, run.ClientName));
        _profiles.Verify(p => p.GetContactLanguageAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _inbox.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        _queue.Complete();
        Assert.False(await Sut().NotifyTestAsync(payload));
        Traced(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed, "queue_closed");
    }

    [Fact]
    public async Task It_never_throws()
    {
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await Sut().NotifyAsync(Payload());
        Traced(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed);

        _profiles.Setup(p => p.GetForClientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        Assert.False(await Sut().NotifyTestAsync(Payload() with { TestSessionId = Guid.NewGuid(), Bot = BotKit.Config }));
    }
}
