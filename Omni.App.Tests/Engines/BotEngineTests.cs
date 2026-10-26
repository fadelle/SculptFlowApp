using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.Ai;
using Omni.App.Business.Services.Ai;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Tests.Support;

namespace Omni.App.Tests.Engines;

/// <summary>
/// BotEngine against the AI assistant workflow (docs/n8n/omni-ai-assistant.json), path by path, with a scripted model:
/// the agent call, memory, tools, the decision switch, the schedule/cancel result switches and the formatter, the reply,
/// handoff and every way a run ends with nothing sent.
/// </summary>
public class BotEngineTests
{
    private static readonly Guid ClientId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ConversationId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid ContactId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid BookingId = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid ServiceId = Guid.Parse("50000000-0000-0000-0000-000000000005");

    private readonly FakeAiModel _model = new();
    private readonly FakeBotTools _tools = new();
    private readonly FakeBotMemory _memory = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly List<(string Step, string Outcome, TraceIds Ids, string Detail)> _steps = new();

    public BotEngineTests()
    {
        _config.SetupGet(c => c.AiBotModel).Returns("gpt-6-luna");
        _config.SetupGet(c => c.AiFormatterModel).Returns("gpt-4o-mini");
        _config.SetupGet(c => c.AiMaxToolIterations).Returns(10);
        _config.SetupGet(c => c.AiMemoryExchanges).Returns(10);
        _tracer.Setup(t => t.Step(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()))
            .Callback((string step, string outcome, TraceIds ids, double? _, object? detail) => _steps.Add((step, outcome, ids, JsonSerializer.Serialize(detail))));
    }

    private BotEngine Sut(IBotToolService? tools = null) =>
        new(_model, tools ?? _tools, _memory, _unitOfWork.Object, _config.Object, _tracer.Object, NullLogger<BotEngine>.Instance);

    private static AiTriggerPayload Trigger(string text = "Hi", string[]? capabilities = null, Guid? testSession = null) => new(
        ClientId, testSession ?? ConversationId, testSession ?? ContactId, Guid.NewGuid(), testSession is null ? "whatsapp" : "test", "text", text,
        ClientName: "Glow", Language: "en", Timezone: "Asia/Beirut", AiProfile: "A salon.",
        Terms: new Dictionary<string, TermForms> { ["booking"] = new("visit", "visits") },
        Bot: new AiBotConfig(BotKit.BotId, "Front desk", "custom", "Be brief.", (IReadOnlyList<string>?)capabilities ?? BotCapability.All.ToList(), "on_request", "en"),
        TestSessionId: testSession);

    private IEnumerable<string> Steps(string step) => _steps.Where(s => s.Step == step).Select(s => s.Outcome);

    private (string Tool, object? Arguments) Single(string tool) => Assert.Single(_tools.Calls.Where(c => c.Tool == tool).Select(c => (c.Tool, c.Arguments)));

    private void NothingSent()
    {
        Assert.DoesNotContain(_tools.Calls, c => c.Tool is "reply" or "handoff");
    }

    // ---------------------------------------------------------------- the agent

    [Fact]
    public async Task A_reply_runs_the_agent_with_the_rendered_prompt_the_bots_tools_and_the_decision_schema_then_sends_the_message()
    {
        _model.Decide("reply", message: "Hello! How can I help?");
        var trigger = Trigger("Hi there", [BotCapability.Knowledge, BotCapability.Bookings]);

        var before = DateTimeOffset.UtcNow;
        await Sut().RunAsync(trigger);
        var after = DateTimeOffset.UtcNow;

        var request = Assert.Single(_model.Requests);
        Assert.Equal("gpt-6-luna", request.Model);
        Assert.Contains(request.Instructions, new[] { before, after }.Select(t => AiPromptTemplate.RenderFile(AiPrompts.AgentSystem, AiPromptValues.ForMessage(trigger, t))));
        Assert.Contains("Glow", request.Instructions);
        Assert.Equal("Hi there", (string?)Assert.Single(request.Input).Json["content"]);
        // Only the bot's tools are offered (update_contact is every bot's), with the n8n descriptions and argument schemas.
        Assert.Equal(["search_knowledge", "get_my_bookings", "update_contact"], request.Tools!.Select(t => t.Name));
        Assert.Equal(AiPromptTemplate.Load(AiPrompts.ToolDescription("search_knowledge")), request.Tools![0].Description);
        Assert.Equal("query", Assert.Single(request.Tools![0].Parameters["required"]!.AsArray())!.GetValue<string>());
        Assert.True(request.Output!.Strict);
        Assert.Equal(7, request.Output.Schema["required"]!.AsArray().Count);

        Assert.Equal((ConversationId, (string?)"Hello! How can I help?"), ((Guid, string?))Single("reply").Arguments!);
        Assert.Equal([TraceOutcomes.Ok], Steps(TraceSteps.BotRunStarted));
        Assert.Equal([TraceOutcomes.Ok], Steps(TraceSteps.BotDecision));
        Assert.Equal([TraceOutcomes.Ok], Steps(TraceSteps.BotActionResult));
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotModelCalled && s.Detail.Contains("\"inputTokens\":100") && s.Detail.Contains("\"role\":\"agent\""));
        Assert.All(_steps, s => Assert.Equal((ClientId, ConversationId), (s.Ids.ClientId, s.Ids.ConversationId)));
        Assert.Empty(Steps(TraceSteps.BotRunFailed));
    }

    [Fact]
    public async Task Tool_calls_loop_with_fixed_workflow_values_and_the_results_go_back_to_the_model()
    {
        _tools.Answers["get_available_slots"] = BotToolResult.Ok(new { configured = true, slots = new[] { new { start = "2026-11-02T14:00:00+02:00" } } });
        _model.Call(("get_available_slots", """{"date":"2026-11-02"}"""), ("get_services", "{}"))
            .Decide("reply", message: "2 PM works.");

        await Sut().RunAsync(Trigger("Monday?"));

        Assert.Equal(2, _model.Requests.Count);
        var (contact, date) = ((Guid?, DateOnly?))Single("get_available_slots").Arguments!;
        Assert.Equal((ContactId, new DateOnly(2026, 11, 2)), (contact, date));
        var second = _model.Requests[1].Input.Select(i => i.Json).ToList();
        Assert.Equal(["function_call", "function_call", "function_call_output", "function_call_output"], second.Skip(1).Select(i => (string?)i["type"]));
        Assert.Equal("""{"configured":true,"slots":[{"start":"2026-11-02T14:00:00+02:00"}]}""", (string?)second[3]["output"]);
        Assert.Equal((string?)second[1]["call_id"], (string?)second[3]["call_id"]);
        Assert.Equal([BotTools.AvailableSlots.Route, BotTools.Services.Route, BotTools.Reply.Route], _tools.Routes);
    }

    [Fact]
    public async Task Tool_arguments_map_like_the_n8n_nodes_and_failures_go_back_to_the_model()
    {
        _tools.Answers["get_services"] = BotToolResult.NotFound();
        _model.Call(("search_knowledge", """{"query":"prices"}"""), ("update_contact", """{"language":"ar"}"""),
                ("update_contact_details", """{"city":"Beirut"}"""), ("update_stage", """{"stage":"booked"}"""),
                ("get_available_slots", """{"date":"next monday"}"""), ("get_services", "{}"), ("delete_everything", "{}"), ("get_my_bookings", "not json"))
            .Decide("reply", message: "ok");

        await Sut().RunAsync(Trigger());

        var search = (KnowledgeSearchRequest)Single("search_knowledge").Arguments!;
        Assert.Equal((ClientId, "prices", (int?)5), (search.ClientId, search.Query, search.Limit));
        var updates = _tools.Calls.Where(c => c.Tool == "update_contact").Select(c => ((Guid, UpdateContactContextRequest))c.Arguments!).ToList();
        Assert.Equal(new UpdateContactContextRequest(null, "ar", null, null, null, null, null), updates[0].Item2);
        Assert.Equal(new UpdateContactContextRequest(null, null, "Beirut", "", "", null, ""), updates[1].Item2); // $fromAI defaults ''
        Assert.Equal("booked", updates[2].Item2.Stage);
        Assert.All(updates, u => Assert.Equal(ContactId, u.Item1));

        var outputs = _model.Requests[1].Input.Select(i => i.Json).Where(j => (string?)j["type"] == "function_call_output").Select(j => (string)j["output"]!).ToList();
        Assert.Contains("\"status\":400", outputs[4]); // a date the endpoint couldn't bind
        Assert.Equal("""{"status":404,"error":null}""", outputs[5]);
        Assert.Contains("Unknown tool delete_everything", outputs[6]);
        Assert.Contains("not valid JSON", outputs[7]);
        Assert.DoesNotContain(_tools.Calls, c => c.Tool == "get_my_bookings");
    }

    [Fact]
    public async Task A_tool_the_bot_lost_is_refused_to_the_model_as_tool_not_allowed()
    {
        _tools.Capabilities = [BotCapability.Bookings]; // availability taken away after the trigger
        _model.Call(("get_available_slots", """{"date":"2026-11-02"}""")).Decide("reply", message: "A team member will help.");

        await Sut().RunAsync(Trigger());

        var output = (string)_model.Requests[1].Input.Last().Json["output"]!;
        Assert.Contains("\"code\":\"TOOL_NOT_ALLOWED\"", output);
        Assert.DoesNotContain(_tools.Calls, c => c.Tool == "get_available_slots");
        Assert.Single(_tools.Calls, c => c.Tool == "reply");
    }

    [Fact]
    public async Task The_agent_stops_after_the_iteration_limit_remembers_it_and_sends_nothing()
    {
        _config.SetupGet(c => c.AiMaxToolIterations).Returns(3);
        _model.KeepCallingTools = true;

        await Sut().RunAsync(Trigger("loop"));

        Assert.Equal(3, _model.Requests.Count);
        Assert.Equal(BotEngine.MaxIterationsOutput, Assert.Single(_memory.Rows).Output);
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotDecision && s.Outcome == TraceOutcomes.Failed && s.Detail.Contains("max_tool_iterations"));
        NothingSent();
    }

    // ---------------------------------------------------------------- memory

    [Fact]
    public async Task Memory_feeds_the_newest_exchanges_and_keeps_the_window()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        for (var i = 0; i < 12; i++)
        {
            _memory.Rows.Add(new BotMemory { Id = Guid.NewGuid(), ClientId = ClientId, ConversationId = ConversationId, UserText = $"q{i}", Output = $"a{i}", CreatedAt = start.AddMinutes(i) });
        }
        _memory.Rows.Add(new BotMemory { Id = Guid.NewGuid(), ClientId = ClientId, ConversationId = Guid.NewGuid(), UserText = "other", Output = "other", CreatedAt = start });
        _model.Decide("reply", message: "Hi again");

        await Sut().RunAsync(Trigger("q12"));

        var input = _model.Requests[0].Input.Select(i => ((string?)i.Json["role"], (string?)i.Json["content"])).ToList();
        Assert.Equal(21, input.Count);
        Assert.Equal(("user", "q2"), input[0]);
        Assert.Equal(("assistant", "a2"), input[1]);
        Assert.Equal(("user", "q12"), input[^1]);

        var kept = _memory.Rows.Where(m => m.ConversationId == ConversationId).OrderBy(m => m.CreatedAt).ToList();
        Assert.Equal(10, kept.Count);
        Assert.Equal("q3", kept[0].UserText);
        Assert.Equal(("q12", JsonSerializer.Serialize(new { action = "reply", message = "Hi again", scheduledStart = (string?)null, confirmReplaceExisting = (bool?)false,
            bookingId = (string?)null, serviceId = (string?)null, reason = (string?)null })), (kept[^1].UserText, kept[^1].Output));
        Assert.Contains(_memory.Rows, m => m.UserText == "other");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_memory_exchanges_the_agent_sees_only_the_message_and_nothing_is_kept()
    {
        _config.SetupGet(c => c.AiMemoryExchanges).Returns(0);
        _memory.Rows.Add(new BotMemory { Id = Guid.NewGuid(), ClientId = ClientId, ConversationId = ConversationId, UserText = "old", Output = "old", CreatedAt = DateTimeOffset.UtcNow });
        _model.Decide("reply", message: "Hi");

        await Sut().RunAsync(Trigger());

        Assert.Single(_model.Requests[0].Input);
        Assert.Empty(_memory.Rows);
    }

    // ---------------------------------------------------------------- the decision

    [Theory]
    [InlineData("not json")]
    [InlineData("```json\n{\"action\":\"reply\",\"message\":\"x\"}\n```")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public async Task Unreadable_output_is_a_failed_decision_remembered_with_nothing_sent(string output)
    {
        _model.Text(output);

        await Sut().RunAsync(Trigger());

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotDecision && s.Outcome == TraceOutcomes.Failed && s.Detail.Contains("unreadable_output"));
        Assert.Equal(output, Assert.Single(_memory.Rows).Output);
        Assert.Empty(_tools.Calls);
    }

    [Theory]
    [InlineData("book")]
    [InlineData("Reply")]
    [InlineData(null)]
    public async Task An_unknown_action_sends_nothing(string? action)
    {
        _model.Text(JsonSerializer.Serialize(new { action, message = "hi" }));

        await Sut().RunAsync(Trigger());

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotDecision && s.Outcome == TraceOutcomes.Failed && s.Detail.Contains("unknown_action"));
        Assert.Empty(_tools.Calls);
    }

    [Fact]
    public async Task A_model_failure_ends_the_run_traced_without_memory_or_messages()
    {
        _model.Throw(new AiModelException("The AI model API answered HTTP 500."));

        await Sut().RunAsync(Trigger());

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotModelCalled && s.Outcome == TraceOutcomes.Failed);
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotRunFailed && s.Detail.Contains("HTTP 500"));
        Assert.Empty(_memory.Rows);
        Assert.Empty(_tools.Calls);
    }

    // ---------------------------------------------------------------- reply

    [Theory]
    [InlineData("conversation_in_human_mode")]
    [InlineData("ai_reply_refused")]
    public async Task A_409_on_the_reply_stops_quietly(string code)
    {
        _tools.Answers["reply"] = BotToolResult.Conflict(new { sent = false, code, conversationId = ConversationId });
        _model.Decide("reply", message: "Hi");

        await Sut().RunAsync(Trigger());

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotReplyStopped && s.Outcome == TraceOutcomes.Skipped && s.Detail.Contains(code));
        Assert.Empty(Steps(TraceSteps.BotRunFailed));
        Assert.Empty(Steps(TraceSteps.BotActionResult));
    }

    [Fact]
    public async Task Another_reply_error_is_a_failed_step()
    {
        _tools.Answers["reply"] = BotToolResult.NotFound();
        _model.Decide("reply", message: "Hi");

        await Sut().RunAsync(Trigger());

        Assert.Equal([TraceOutcomes.Failed], Steps(TraceSteps.BotActionResult));
    }

    // ---------------------------------------------------------------- schedule

    public static TheoryData<string> ScheduleCodes => new(CannedToolResults.ScheduleCodes);

    [Theory]
    [MemberData(nameof(ScheduleCodes))]
    public async Task Every_schedule_result_is_worded_by_the_formatter_and_sent(string code)
    {
        if (code == BotToolOutcomes.NotAllowedCode) _tools.Capabilities = [BotCapability.Knowledge];
        else if (code == BotToolOutcomes.FeatureNotIncludedCode) _tools.BookingsIncluded = false;
        else _tools.Answers["schedule"] = BotToolResult.Ok(CannedToolResults.Schedule(code));
        _model.Decide("schedule", scheduledStart: "2026-09-29T16:00:00+03:00", confirmReplaceExisting: true, bookingId: BookingId.ToString(),
                serviceId: ServiceId.ToString(), reason: "moving")
            .Text($"Formatted for {code}");
        var trigger = Trigger("Tuesday 4 please");

        await Sut().RunAsync(trigger);

        var formatter = _model.Requests[1];
        Assert.Equal("gpt-4o-mini", formatter.Model);
        Assert.Null(formatter.Tools);
        Assert.Null(formatter.Output);
        var expected = CannedToolResults.Schedule(code);
        var values = AiPromptValues.ForFormatter(trigger, DateTimeOffset.UtcNow, expected);
        Assert.Equal(AiPromptTemplate.RenderFile(AiPrompts.FormatterSystem, values), formatter.Instructions);
        Assert.Equal(AiPromptTemplate.RenderFile(AiPrompts.FormatterUser, values), (string?)Assert.Single(formatter.Input).Json["content"]);
        Assert.StartsWith("VERIFIED BACKEND RESULT:\n" + JsJson.Stringify(expected) + "\n\nLATEST CONTACT MESSAGE:\nTuesday 4 please",
            (string?)formatter.Input[0].Json["content"]);
        Assert.Equal($"Formatted for {code}", ((ValueTuple<Guid, string?>)Single("reply").Arguments!).Item2);
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotActionResult && s.Outcome == TraceOutcomes.Ok && s.Detail.Contains($"\"code\":\"{code}\""));
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotModelCalled && s.Detail.Contains("\"role\":\"formatter\""));
    }

    [Fact]
    public async Task The_schedule_call_carries_the_decision_like_the_workflow_body()
    {
        _tools.Answers["schedule"] = BotToolResult.Ok(CannedToolResults.Schedule("BOOKED"));
        _model.Decide("schedule", scheduledStart: "2026-09-29T16:00:00+03:00", confirmReplaceExisting: true, bookingId: "", serviceId: ServiceId.ToString(), reason: "")
            .Text("Booked!");

        await Sut().RunAsync(Trigger());

        var (contact, request) = ((Guid, ScheduleBookingRequest))Single("schedule").Arguments!;
        Assert.Equal(ContactId, contact);
        Assert.Equal(new ScheduleBookingRequest(DateTimeOffset.Parse("2026-09-29T16:00:00+03:00"), ServiceId, null, "", null, true, null), request);
    }

    [Fact]
    public async Task A_success_with_an_unrouted_code_goes_nowhere()
    {
        _tools.Answers["schedule"] = BotToolResult.Ok(new { success = true, code = "SOMETHING_NEW" });
        _model.Decide("schedule", scheduledStart: "2026-09-29T16:00:00+03:00");

        await Sut().RunAsync(Trigger());

        Assert.Single(_model.Requests);
        Assert.Equal([TraceOutcomes.Skipped], Steps(TraceSteps.BotActionResult));
        NothingSent();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("tomorrow at 4", null, null)]
    [InlineData("2026-09-29T16:00:00+03:00", "not-an-id", null)]
    [InlineData("2026-09-29T16:00:00+03:00", null, "booking-1")]
    public async Task A_schedule_the_endpoint_could_not_bind_sends_nothing(string? start, string? serviceId, string? bookingId)
    {
        _model.Decide("schedule", scheduledStart: start, serviceId: serviceId, bookingId: bookingId);

        await Sut().RunAsync(Trigger());

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotActionResult && s.Outcome == TraceOutcomes.Failed && s.Detail.Contains("invalid_request"));
        Assert.Empty(_tools.Calls);
    }

    [Fact]
    public async Task An_error_answer_from_the_schedule_tool_sends_nothing()
    {
        _tools.Answers["schedule"] = BotToolResult.BadRequest(new { error = "bad" });
        _model.Decide("schedule", scheduledStart: "2026-09-29T16:00:00+03:00");

        await Sut().RunAsync(Trigger());

        Assert.Single(_model.Requests);
        Assert.Equal([TraceOutcomes.Failed], Steps(TraceSteps.BotActionResult));
        NothingSent();
    }

    // ---------------------------------------------------------------- cancel

    public static TheoryData<string> CancelCodes => new(CannedToolResults.CancelCodes);

    [Theory]
    [MemberData(nameof(CancelCodes))]
    public async Task Every_cancel_result_is_worded_by_the_formatter_and_sent(string code)
    {
        if (code == BotToolOutcomes.NotAllowedCode) _tools.Capabilities = [BotCapability.Knowledge];
        else if (code == BotToolOutcomes.FeatureNotIncludedCode) _tools.BookingsIncluded = false;
        else _tools.Answers["cancel"] = BotToolResult.Ok(CannedToolResults.Cancel(code));
        _model.Decide("cancel", bookingId: BookingId.ToString(), reason: "sick").Text($"Cancel says {code}");

        await Sut().RunAsync(Trigger("cancel it"));

        Assert.StartsWith("VERIFIED BACKEND RESULT:\n" + JsJson.Stringify(CannedToolResults.Cancel(code)), (string?)_model.Requests[1].Input[0].Json["content"]);
        Assert.Equal($"Cancel says {code}", ((ValueTuple<Guid, string?>)Single("reply").Arguments!).Item2);
    }

    [Fact]
    public async Task The_cancel_call_carries_the_booking_and_reason_or_nulls()
    {
        _tools.Answers["cancel"] = BotToolResult.Ok(CannedToolResults.Cancel("CANCELED"));
        _model.Decide("cancel", bookingId: BookingId.ToString(), reason: "sick").Text("Done.")
            .Decide("cancel", bookingId: "", reason: "").Text("Done.");

        await Sut().RunAsync(Trigger());
        await Sut().RunAsync(Trigger());

        var calls = _tools.Calls.Where(c => c.Tool == "cancel").Select(c => ((Guid, Guid?, CancelBookingRequest))c.Arguments!).ToList();
        Assert.Equal((ContactId, (Guid?)BookingId, "sick"), (calls[0].Item1, calls[0].Item2, calls[0].Item3.Reason));
        Assert.Equal(((Guid?)null, (string?)null), (calls[1].Item2, calls[1].Item3.Reason));
    }

    // ---------------------------------------------------------------- handoff

    [Theory]
    [InlineData("Wants a refund", "Wants a refund")]
    [InlineData("", BotEngine.HandoffFallbackReason)]
    [InlineData(null, BotEngine.HandoffFallbackReason)]
    public async Task A_handoff_hands_over_with_the_reason_or_the_workflows_default_and_sends_nothing_else(string? reason, string expected)
    {
        _model.Decide("handoff", reason: reason);

        await Sut().RunAsync(Trigger());

        Assert.Equal((ConversationId, (string?)expected), ((Guid, string?))Single("handoff").Arguments!);
        Assert.DoesNotContain(_tools.Calls, c => c.Tool == "reply");
        Assert.Single(_model.Requests);
        Assert.Equal([TraceOutcomes.Ok], Steps(TraceSteps.BotActionResult));
    }

    // ---------------------------------------------------------------- test conversations

    [Fact]
    public async Task A_test_run_uses_the_test_session_everywhere_and_its_writes_are_simulated()
    {
        var session = Guid.NewGuid();
        var tests = new Mock<IBotTestService>();
        var simulated = new ScheduleBookingResult(true, "created", "BOOKED", "Booking made.");
        tests.Setup(t => t.ScheduleAsync(It.Is<BotRunContext>(r => r.TestSessionId == session), It.IsAny<ScheduleBookingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(simulated);
        var conversations = new Mock<IBotConversationService>();
        conversations.Setup(c => c.ReplyAsync(It.IsAny<BotRunContext>(), session, "Booked (test)", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BotReplyOutcome(Guid.NewGuid(), false));
        var runtime = new Mock<IBotRuntimeService>();
        runtime.Setup(r => r.AuthorizeAsync(ClientId, BotKit.BotId, session, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BotToolCheck.Allowed(new BotRunContext(ClientId, BotKit.BotId, new HashSet<string>(BotCapability.All), null, null, session)));
        var bookings = new Mock<IBookingService>();
        var tools = new BotToolService(Mock.Of<IClientContext>(), Mock.Of<ICatalogService>(), Mock.Of<IContactService>(), bookings.Object,
            Mock.Of<IAvailabilityService>(), Mock.Of<IKnowledgeSearchService>(), runtime.Object, tests.Object, conversations.Object,
            Mock.Of<IEntitlementService>(), _tracer.Object);
        _model.Decide("schedule", scheduledStart: "2026-09-29T16:00:00+03:00").Text("Booked (test)");

        await Sut(tools).RunAsync(Trigger("book", testSession: session));

        tests.VerifyAll();
        conversations.VerifyAll();
        bookings.Verify(b => b.ScheduleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ScheduleBookingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var memory = Assert.Single(_memory.Rows);
        Assert.Equal(((Guid?)null, (Guid?)session), (memory.ConversationId, memory.TestSessionId));
        Assert.All(_steps, s => Assert.Null(s.Ids.ConversationId));
        Assert.Contains(_steps, s => s.Step == TraceSteps.BotToolCalled && s.Detail.Contains("bookings/schedule"));
    }

    [Fact]
    public async Task A_trigger_without_a_bot_is_a_failed_run()
    {
        await Sut().RunAsync(Trigger() with { Bot = null });

        Assert.Contains(_steps, s => s.Step == TraceSteps.BotRunFailed);
        Assert.Empty(_model.Requests);
    }
}
