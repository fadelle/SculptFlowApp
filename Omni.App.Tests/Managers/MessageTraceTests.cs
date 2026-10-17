using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Services.Monitoring;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Business.Jobs;
using Omni.App.Business.Managers;
using Omni.App.Business.Mappers.PlatformAdmin;
using Omni.App.Business.Services.Monitoring;
using Omni.App.Business.Services.PlatformAdmin;
using Omni.App.Controllers.Admin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Persistence.Contracts.Monitoring;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.Managers;

/// <summary>The in-memory recorder: what a step carries, the on/off switch, the bounded buffer, and that it never throws.</summary>
public class MessageTracerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IConfigManager> _config = new();
    private readonly HttpContextAccessor _http = new();

    public MessageTracerTests() => _config.SetupGet(c => c.MessageTraceEnabled).Returns(true);

    private MessageTracer Sut() => new(_config.Object, _http, new ManualTimeProvider(Now));

    [Fact]
    public void A_step_carries_the_current_trace_its_ids_detail_and_the_n8n_execution()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[MessageTraceLimits.N8nExecutionHeader] = "exec-42";
        _http.HttpContext = context;
        var ids = new TraceIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "wamid.1");
        var sut = Sut();
        using var activity = new Activity("t").Start();

        sut.Step(TraceSteps.BotTriggered, TraceOutcomes.Ok, ids, 12.345, new { status = 200, reason = "x" });

        var row = Assert.Single(sut.Drain(10));
        Assert.Equal((activity.TraceId.ToHexString(), Now, TraceSteps.BotTriggered, TraceOutcomes.Ok, 12.3),
            (row.TraceId, row.OccurredAt, row.Step, row.Outcome, row.DurationMs!.Value));
        Assert.Equal((ids.ClientId, ids.ConversationId, ids.MessageId, "whatsapp", "wamid.1", "exec-42"),
            (row.ClientId, row.ConversationId, row.MessageId, row.Channel, row.ExternalMessageId, row.N8nExecutionId));
        Assert.Equal("{\"status\":200,\"reason\":\"x\"}", row.Detail);
        Assert.Equal(AppLogLimits.InstanceName, row.Instance);
        Assert.Equal(0, sut.Pending);
    }

    [Fact]
    public void Outside_a_trace_each_step_gets_its_own_id_and_no_detail_is_an_empty_object()
    {
        Activity.Current = null;
        var sut = Sut();
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds());
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds());
        var rows = sut.Drain(10);
        Assert.All(rows, r => Assert.Equal(32, r.TraceId.Length));
        Assert.NotEqual(rows[0].TraceId, rows[1].TraceId);
        Assert.Equal(("{}", (double?)null, (string?)null), (rows[0].Detail, rows[0].DurationMs, rows[0].N8nExecutionId));
    }

    [Fact]
    public void Off_records_nothing_and_a_huge_or_broken_detail_never_throws()
    {
        var sut = Sut();
        _config.SetupGet(c => c.MessageTraceEnabled).Returns(false);
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds());
        Assert.Equal(0, sut.Pending);

        _config.SetupGet(c => c.MessageTraceEnabled).Returns(true);
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds(), detail: new { big = new string('x', MessageTraceLimits.MaxDetailLength) });
        Assert.Equal("{\"_truncated\":true}", Assert.Single(sut.Drain(10)).Detail);

        var cyclic = new Cycle();
        cyclic.Self = cyclic;
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds(), detail: cyclic); // serialization throws inside
        Assert.Equal(0, sut.Pending);

        _config.SetupGet(c => c.MessageTraceEnabled).Throws(new InvalidOperationException("config broken"));
        sut.Step(TraceSteps.WebhookReceived, TraceOutcomes.Ok, new TraceIds());
    }

    [Fact]
    public void A_full_buffer_drops_and_counts_and_requeue_puts_steps_back()
    {
        var sut = Sut();
        for (var i = 0; i < MessageTraceLimits.BufferCapacity + 7; i++) sut.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, new TraceIds());
        Assert.Equal(MessageTraceLimits.BufferCapacity, sut.Pending);
        Assert.Equal(7, sut.TakeDropped());
        Assert.Equal(0, sut.TakeDropped());

        var batch = sut.Drain(3);
        Assert.Equal(MessageTraceLimits.BufferCapacity - 3, sut.Pending);
        sut.Requeue(batch);
        Assert.Equal(MessageTraceLimits.BufferCapacity, sut.Pending);
    }

    [Fact]
    public void A_new_trace_is_a_root_and_restores_the_previous_one()
    {
        using var outer = new Activity("outer").Start();
        Assert.Equal(outer.TraceId.ToHexString(), TraceContext.CurrentTraceId());
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-01$", TraceContext.CurrentTraceparent()!);

        string inner;
        using (TraceContext.StartNew("campaign.send"))
        {
            inner = TraceContext.CurrentTraceId()!;
            Assert.NotEqual(outer.TraceId.ToHexString(), inner);
        }
        Assert.Same(outer, Activity.Current);

        Activity.Current = null;
        Assert.Null(TraceContext.CurrentTraceId());
        Assert.Null(TraceContext.CurrentTraceparent());
    }

    private sealed class Cycle
    {
        public Cycle? Self { get; set; }
    }
}

/// <summary>Saving, trimming and flushing the trace steps.</summary>
public class MessageTraceServiceAndJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Save_writes_rows_and_purge_uses_both_retentions()
    {
        var repo = new Mock<IMessageTraceStepRepository>();
        var unit = new Mock<IUnitOfWork>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.MessageTraceRetentionDays).Returns(14);
        config.SetupGet(c => c.MessageTraceFailedRetentionDays).Returns(60);
        repo.Setup(r => r.DeleteExpiredAsync(Now.AddDays(-14), Now.AddDays(-60), It.IsAny<CancellationToken>())).ReturnsAsync(9).Verifiable();
        var sut = new MessageTraceService(repo.Object, unit.Object, config.Object, new ManualTimeProvider(Now));

        await sut.SaveAsync([], default);
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        var row = new MessageTraceStep { TraceId = "t", Step = "s", Outcome = "ok" };
        await sut.SaveAsync([row], default);
        repo.Verify(r => r.AddRange(It.Is<IEnumerable<MessageTraceStep>>(rows => rows.Single() == row)));
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(9, await sut.PurgeAsync(default));
        repo.Verify();
    }

    private static (MessageTraceFlushJob Job, Mock<IMessageTraceService> Service, MessageTracer Tracer, ManualTimeProvider Clock, List<string> Logged) Job()
    {
        var clock = new ManualTimeProvider(Now);
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.MessageTraceEnabled).Returns(true);
        var tracer = new MessageTracer(config.Object, new HttpContextAccessor(), clock);
        var service = new Mock<IMessageTraceService>();
        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var logged = new List<string>();
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<MessageTraceFlushJob>>();
        logger.Setup(l => l.Log(It.IsAny<Microsoft.Extensions.Logging.LogLevel>(), It.IsAny<Microsoft.Extensions.Logging.EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(i => logged.Add(i.Arguments[2]!.ToString()!)));
        return (new MessageTraceFlushJob(tracer, services.GetRequiredService<IServiceScopeFactory>(), clock, logger.Object), service, tracer, clock, logged);
    }

    [Fact]
    public async Task Flush_writes_batches_and_purges_once_per_interval()
    {
        var (job, service, tracer, clock, _) = Job();
        for (var i = 0; i < MessageTraceLimits.BatchSize + 2; i++) tracer.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, new TraceIds());

        await job.FlushAsync(default);
        service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<MessageTraceStep>>(r => r.Count == MessageTraceLimits.BatchSize), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<MessageTraceStep>>(r => r.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Once);

        clock.Advance(TimeSpan.FromMinutes(1));
        await job.FlushAsync(default);
        service.Verify(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<MessageTraceStep>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        clock.Advance(TimeSpan.FromMinutes(MessageTraceLimits.PurgeIntervalMinutes));
        await job.FlushAsync(default);
        service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_failed_write_is_retried_dropped_steps_are_logged_and_stop_flushes()
    {
        var (job, service, tracer, clock, logged) = Job();
        service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<MessageTraceStep>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db"));
        tracer.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, new TraceIds());
        await job.FlushAsync(default);
        Assert.Equal(1, tracer.Pending);
        Assert.Contains(logged, l => l.Contains("will be retried"));

        service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<MessageTraceStep>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await job.StopAsync(default);
        Assert.Equal(0, tracer.Pending);

        for (var i = 0; i < MessageTraceLimits.BufferCapacity + 1; i++) tracer.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, new TraceIds());
        service.Setup(s => s.PurgeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("slow"));
        clock.Advance(TimeSpan.FromMinutes(MessageTraceLimits.PurgeIntervalMinutes));
        await job.FlushAsync(default);
        Assert.Contains(logged, l => l.Contains("1 step(s) were dropped"));
        Assert.Contains(logged, l => l.Contains("flush failed"));

        service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<MessageTraceStep>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        tracer.Step(TraceSteps.InboxNotified, TraceOutcomes.Ok, new TraceIds());
        await Assert.ThrowsAsync<OperationCanceledException>(() => job.FlushAsync(default));
    }
}

/// <summary>The bot's side of the trace: the n8n trigger and the tool calls n8n makes back.</summary>
public class BotTraceTests
{
    private readonly FakeHttpHandler _http = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly List<(string Step, string Outcome, string Detail)> _steps = new();
    private readonly Dictionary<string, string?> _settings = new() { ["N8n:AiWebhookUrl"] = "https://n8n.example/webhook/ai" };

    public BotTraceTests()
    {
        _tracer.Setup(t => t.Step(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()))
            .Callback<string, string, TraceIds, double?, object?>((s, o, _, _, d) => _steps.Add((s, o, JsonSerializer.Serialize(d))));
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
    }

    private AiTriggerNotifier Notifier() => new(_http.CreateClient(), new ConfigurationBuilder().AddInMemoryCollection(_settings).Build(),
        NullLogger<AiTriggerNotifier>.Instance, _entitlements.Object, _tracer.Object);

    private static AiTriggerPayload Payload() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "whatsapp", "text", "hi");

    [Fact]
    public async Task The_trigger_sends_the_traceparent_and_records_the_outcome()
    {
        using var activity = new Activity("t").Start();
        _http.Enqueue(HttpStatusCode.OK, "{}");
        await Notifier().NotifyAsync(Payload());
        Assert.Contains($"\"traceparent\":\"00-{activity.TraceId.ToHexString()}-", Assert.Single(_http.Requests).Body);
        Assert.Equal((TraceSteps.BotTriggered, TraceOutcomes.Ok), (_steps[^1].Step, _steps[^1].Outcome));

        _http.Enqueue(HttpStatusCode.BadGateway, "no");
        await Notifier().NotifyAsync(Payload());
        Assert.Equal((TraceSteps.BotTriggerFailed, "{\"status\":502}"), (_steps[^1].Step, _steps[^1].Detail));

        _http.EnqueueThrow(new HttpRequestException("down"));
        await Notifier().NotifyAsync(Payload());
        Assert.Contains("down", _steps[^1].Detail);

        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, true, "active", "basic", "Basic", null, new Dictionary<string, string>()));
        await Notifier().NotifyAsync(Payload());
        Assert.Contains("plan_without_ai_assistant", _steps[^1].Detail);

        _settings.Remove("N8n:AiWebhookUrl");
        await Notifier().NotifyAsync(Payload());
        Assert.Contains("n8n_not_configured", _steps[^1].Detail);
        Assert.All(_steps.Skip(3), s => Assert.Equal(TraceOutcomes.Skipped, s.Outcome));
    }

    private sealed record ToolRequest(Guid ClientId, Guid ConversationId, string Text);

    private static ActionExecutingContext Context(IServiceProvider? services, string template, Dictionary<string, object?> arguments)
    {
        var http = new DefaultHttpContext();
        if (services is not null) http.RequestServices = services;
        var descriptor = new ControllerActionDescriptor { AttributeRouteInfo = new AttributeRouteInfo { Template = template }, DisplayName = "tool" };
        return new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor), [], arguments, new object());
    }

    [Fact]
    public async Task Every_tool_call_is_recorded_with_its_ids_status_and_outcome()
    {
        var services = new ServiceCollection().AddSingleton(_tracer.Object).BuildServiceProvider();
        var ids = new List<TraceIds>();
        _tracer.Setup(t => t.Step(TraceSteps.BotToolCalled, It.IsAny<string>(), It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()))
            .Callback<string, string, TraceIds, double?, object?>((s, o, i, _, d) => { ids.Add(i); _steps.Add((s, o, JsonSerializer.Serialize(d))); });
        var filter = new TraceBotToolAttribute();
        var clientId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        var ok = Context(services, "api/ai/knowledge/search", new() { ["request"] = new ToolRequest(clientId, conversationId, "q") });
        await filter.OnActionExecutionAsync(ok, () => Task.FromResult(new ActionExecutedContext(ok, [], new object()) { Result = new OkObjectResult(1) }));
        Assert.Equal((TraceOutcomes.Ok, clientId, conversationId), (_steps[^1].Outcome, ids[^1].ClientId!.Value, ids[^1].ConversationId!.Value));
        Assert.Contains("\"tool\":\"api/ai/knowledge/search\",\"status\":200", _steps[^1].Detail);

        var handoff = Context(services, "api/ai/conversations/{id:guid}/handoff", new() { ["id"] = conversationId, ["clientId"] = clientId });
        await filter.OnActionExecutionAsync(handoff, () => Task.FromResult(new ActionExecutedContext(handoff, [], new object()) { Result = new NotFoundResult() }));
        Assert.Equal((TraceOutcomes.Failed, conversationId), (_steps[^1].Outcome, ids[^1].ConversationId!.Value));

        var contact = Context(services, "api/ai/contacts/{contactId:guid}", new() { ["contactId"] = Guid.NewGuid() });
        await filter.OnActionExecutionAsync(contact, () => Task.FromResult(new ActionExecutedContext(contact, [], new object()) { Exception = new Exception("boom") }));
        Assert.Contains("\"status\":500", _steps[^1].Detail);
        Assert.Null(ids[^1].ConversationId); // "id" only counts for conversation routes

        var none = Context(null, "x", new());
        await filter.OnActionExecutionAsync(none, () => Task.FromResult(new ActionExecutedContext(none, [], new object())));
        Assert.Equal(3, ids.Count);
    }
}

/// <summary>The platform-admin timeline: assembly from every source, search, list, counts.</summary>
public class MessageTraceAdminTests
{
    private const string Trace = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IMessageTraceAdminRepository> _repo = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly Guid _inboundId = Guid.NewGuid();
    private readonly Guid _replyId = Guid.NewGuid();

    private MessageTraceAdminService Sut()
    {
        _config.SetupGet(c => c.MessageTraceEnabled).Returns(true);
        _config.SetupGet(c => c.MessageTraceRetentionDays).Returns(14);
        _config.SetupGet(c => c.MessageTraceFailedRetentionDays).Returns(60);
        return new MessageTraceAdminService(_repo.Object, _config.Object, new ManualTimeProvider(T0.AddHours(1)));
    }

    private TraceStepRow Step(int second, string step, string outcome = TraceOutcomes.Ok, Guid? messageId = null, string detail = "{}") =>
        new(second, Trace, T0.AddSeconds(second), step, outcome, null, step == TraceSteps.WebhookReceived ? null : _clientId,
            step == TraceSteps.WebhookReceived ? null : _conversationId, messageId, null, "whatsapp", null, detail);

    private void SeedStory()
    {
        _repo.Setup(r => r.StepsOfTraceReadOnlyAsync(Trace, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Step(0, TraceSteps.WebhookReceived, detail: "{\"events\":1,\"kinds\":\"CustomerMessage\"}"),
            Step(1, TraceSteps.MessageSaved, messageId: _inboundId, detail: "{\"sender\":\"contact\",\"type\":\"text\",\"direction\":\"inbound\"}"),
            Step(2, TraceSteps.BotTriggered, detail: "{\"status\":200}"),
            Step(5, TraceSteps.BotToolCalled, detail: "{\"tool\":\"api/ai/knowledge/search\",\"status\":200}"),
            Step(8, TraceSteps.SendProviderAccepted, messageId: _replyId)
        ]);
        _repo.Setup(r => r.MessagesReadOnlyAsync(Trace, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new TraceMessageRow(_inboundId, _clientId, _conversationId, "inbound", "contact", "whatsapp_customer", "whatsapp", "text", "wamid.in", null,
                T0.AddSeconds(1), null, null, null, null, null, null, Trace),
            new TraceMessageRow(_replyId, _clientId, _conversationId, "outbound", "ai", "ai", "whatsapp", "text", "wamid.out", "failed",
                T0.AddSeconds(8), T0.AddSeconds(8), T0.AddSeconds(20), null, T0.AddMinutes(3), "131047", "Re-engagement window", Trace)
        ]);
        _repo.Setup(r => r.LogsOfTraceReadOnlyAsync(Trace, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new AppLogRow(77, T0.AddSeconds(6), "Error", "Omni.App.X", "Lookup failed", "System.TimeoutException", "fp", 1, Trace, _clientId, null, null, "POST", "/api/ai/x")]);
        _repo.Setup(r => r.BillingReadOnlyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(
            [new TraceBillingRow(_replyId, ChargeStatus.Released, 0.05m, "USD", T0.AddSeconds(7), T0.AddSeconds(7), null, T0.AddMinutes(3), "delivery_failed", null)]);
        _repo.Setup(r => r.EventsReadOnlyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), T0.AddMinutes(-1), T0.AddSeconds(8).AddMinutes(MessageTraceLimits.EventWindowMinutes), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TraceEventRow(T0.AddSeconds(1), "contact_message_received", "whatsapp_customer", _conversationId, "{}")]);
    }

    [Fact]
    public async Task The_timeline_merges_steps_messages_statuses_billing_events_and_errors_in_order()
    {
        SeedStory();
        var trace = await Sut().GetAsync(" " + Trace.ToUpperInvariant() + " ", default);

        Assert.Equal((Trace, _clientId, true), (trace.TraceId, trace.ClientId, trace.HasFailure));
        Assert.Equal([_conversationId], trace.ConversationIds);
        Assert.Equal([_inboundId, _replyId], trace.MessageIds);
        Assert.Equal(T0, trace.StartedAt);
        Assert.Equal(T0.AddMinutes(3), trace.EndedAt); // the failed delivery came later than the last step
        Assert.Equal(
            ["webhook.received", "message.saved", "message.inbound", "contact_message_received", "bot.triggered", "bot.tool_called", "Omni.App.X",
             "billing.reserved", "send.provider_accepted", "message.outbound", "status.delivered", "billing.released", "status.failed"],
            trace.Entries.Select(e => e.Name));
        var failed = trace.Entries.Single(e => e.Name == "status.failed");
        Assert.Equal((TraceOutcomes.Failed, "Delivery failed (131047): Re-engagement window"), (failed.Outcome, failed.Summary));
        Assert.Equal(77, trace.Entries.Single(e => e.Kind == "error").LogId);
        Assert.Equal("Webhook received (whatsapp): CustomerMessage", trace.Entries[0].Summary);
        Assert.Equal("Bot used tool api/ai/knowledge/search (answered 200)", trace.Entries.Single(e => e.Name == "bot.tool_called").Summary);
        Assert.Equal("Charge released: delivery_failed", trace.Entries.Single(e => e.Name == "billing.released").Summary);
        Assert.Equal("200", trace.Entries.Single(e => e.Name == "bot.triggered").Detail["status"]);
    }

    [Fact]
    public async Task Unknown_or_malformed_traces_are_not_found()
    {
        _repo.Setup(r => r.StepsOfTraceReadOnlyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repo.Setup(r => r.MessagesReadOnlyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _repo.Setup(r => r.LogsOfTraceReadOnlyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync(Trace, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync("not-a-trace", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ForMessageAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_message_opens_the_trace_it_was_created_in()
    {
        SeedStory();
        _repo.Setup(r => r.TraceIdOfMessageReadOnlyAsync(_replyId, It.IsAny<CancellationToken>())).ReturnsAsync(Trace);
        Assert.Equal(Trace, (await Sut().ForMessageAsync(_replyId, default)).TraceId);
    }

    [Fact]
    public async Task Search_accepts_a_trace_id_a_message_id_or_a_provider_id()
    {
        _repo.Setup(r => r.TraceIdOfMessageReadOnlyAsync(_replyId, It.IsAny<CancellationToken>())).ReturnsAsync(Trace);
        _repo.Setup(r => r.TraceIdOfExternalMessageReadOnlyAsync("wamid.out", It.IsAny<CancellationToken>())).ReturnsAsync(Trace);
        var sut = Sut();
        Assert.Equal(Trace, (await sut.ResolveAsync(Trace.ToUpperInvariant(), default)).TraceId);
        Assert.Equal(Trace, (await sut.ResolveAsync(_replyId.ToString(), default)).TraceId);
        Assert.Equal(Trace, (await sut.ResolveAsync(" wamid.out ", default)).TraceId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ResolveAsync("wamid.unknown", default));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.ResolveAsync(" ", default));
    }

    [Fact]
    public async Task List_and_summary_clamp_and_pass_the_filter()
    {
        var row = new TraceSummaryRow(Trace, T0, T0, 5, 1, 0, _clientId, _conversationId, "whatsapp", "webhook.received", "send.provider_failed", "{}");
        _repo.Setup(r => r.ListReadOnlyAsync(new MessageTraceFilter(T0.AddHours(1).AddHours(-24), _clientId, null, true), 50, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([row], 51)).Verifiable();
        var page = await Sut().ListAsync(24, _clientId, null, true, 2, 50, default);
        _repo.Verify();
        Assert.Equal((2, 50, 51, true, 14, 60), (page.Page, page.PageSize, page.Total, page.Enabled, page.RetentionDays, page.FailedRetentionDays));

        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<MessageTraceFilter>(), 0, MessageTraceLimits.MaxPageSize, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0)).Verifiable();
        Assert.Equal(1, (await Sut().ListAsync(-1, null, null, false, 0, 100_000, default)).Page);
        _repo.Verify();

        _repo.Setup(r => r.CountSinceReadOnlyAsync(T0.AddHours(1).AddHours(-MessageTraceLimits.MaxReportHours), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceCounts(10, 2, T0));
        var summary = await Sut().SummaryAsync(1_000_000, default);
        Assert.Equal((MessageTraceLimits.MaxReportHours, 10L, 2L, (DateTimeOffset?)T0, true), (summary.Hours, summary.Traces, summary.FailedTraces, summary.LastFailedAt, summary.Enabled));
    }

    [Fact]
    public void Every_step_has_a_plain_description()
    {
        foreach (var step in typeof(TraceSteps).GetFields().Select(f => (string)f.GetValue(null)!))
        {
            Assert.NotEqual(step, MessageTraceMapper.Describe(step, "telegram", new Dictionary<string, string?> { ["reason"] = "r", ["error"] = "e" }));
        }
        Assert.Equal("custom.step", MessageTraceMapper.Describe("custom.step", null, new Dictionary<string, string?>()));
    }

    [Fact]
    public void Inbound_messages_have_no_delivery_lines_and_charges_show_their_outcome()
    {
        var id = Guid.NewGuid();
        var entries = MessageTraceMapper.Timeline([],
            [new TraceMessageRow(id, _clientId, _conversationId, "inbound", "contact", "o", "telegram", "text", null, "read", T0, null, T0, T0, null, null, null, Trace),
             new TraceMessageRow(Guid.NewGuid(), _clientId, _conversationId, "outbound", "member", "o", "telegram", "text", null, "read", T0, T0, null, T0.AddSeconds(5), null, null, null, Trace)],
            [new TraceBillingRow(id, ChargeStatus.Settled, 0.1m, "USD", T0, T0, T0.AddSeconds(1), null, null, null),
             new TraceBillingRow(id, ChargeStatus.Failed, null, "USD", T0, null, null, null, null, "insufficient_funds"),
             new TraceBillingRow(id, ChargeStatus.NotCharged, null, "USD", T0, null, null, null, null, null)],
            [], [new AppLogRow(1, T0, "Warning", "C", "slow", null, "fp", 1, Trace, null, null, null, null, null)]);
        Assert.Equal(["message.inbound", "message.outbound", "billing.reserved", "billing.failed", "billing.not_charged", "warning", "billing.charged", "status.read"],
            entries.Select(e => e.Kind == "warning" ? "warning" : e.Name));
        Assert.Equal("Charged 0.1 USD", entries.Single(e => e.Name == "billing.charged").Summary);
        Assert.Equal(TraceOutcomes.Ok, entries.Single(e => e.Kind == "warning").Outcome);
        Assert.Equal(TraceOutcomes.Failed, entries.Single(e => e.Name == "billing.failed").Outcome);
    }

    [Fact]
    public async Task Controller_applies_defaults_for_missing_query_values()
    {
        var service = new Mock<IMessageTraceAdminService>();
        var controller = new MessageTracesAdminController(service.Object);
        var id = Guid.NewGuid();

        Assert.IsType<OkObjectResult>(await controller.List(0, null, null, false, 0, 0, default));
        service.Verify(s => s.ListAsync(MessageTraceLimits.DefaultReportHours, null, null, false, 1, MessageTraceLimits.DefaultPageSize, It.IsAny<CancellationToken>()));
        Assert.IsType<OkObjectResult>(await controller.Summary(0, default));
        service.Verify(s => s.SummaryAsync(MessageTraceLimits.DefaultReportHours, It.IsAny<CancellationToken>()));
        Assert.IsType<OkObjectResult>(await controller.Resolve(null, default));
        service.Verify(s => s.ResolveAsync("", It.IsAny<CancellationToken>()));
        Assert.IsType<OkObjectResult>(await controller.ForMessage(id, default));
        service.Verify(s => s.ForMessageAsync(id, It.IsAny<CancellationToken>()));
        Assert.IsType<OkObjectResult>(await controller.Get(Trace, default));
        service.Verify(s => s.GetAsync(Trace, It.IsAny<CancellationToken>()));
    }
}
