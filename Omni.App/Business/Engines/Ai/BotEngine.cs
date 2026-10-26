using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Omni.App.Business.Contracts.Engines.Ai;
using Omni.App.Business.Contracts.HttpClients.OpenAi;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Ai;

namespace Omni.App.Business.Engines.Ai;

/// <summary>
/// The AI assistant workflow (docs/n8n/omni-ai-assistant.json) as code, node by node:
/// <list type="number">
/// <item>"AI Agent": model Ai / BotModel, system prompt AiPrompts.AgentSystem, the conversation's memory ("Simple Memory":
/// the newest Ai / MemoryExchanges exchanges, key = the trigger's conversationId), input = the message text, the tools the
/// bot may use (each run through IBotToolService.CallAsync, so the bot check, plan feature, test simulation and trace step
/// are the /api/ai ones), at most Ai / MaxToolIterations model calls. The exchange is remembered as soon as the agent
/// finishes, whatever happens next.</item>
/// <item>"llm_result" + "action_needed": the final output parsed as the decision JSON; reply / schedule / cancel / handoff,
/// anything else (or unreadable output) ends the run with nothing sent.</item>
/// <item>reply → "send_message" with the decision's message.</item>
/// <item>schedule → "schedule_booking" ({scheduledStart, serviceId || null, notes "", reason || null,
/// confirmReplaceExisting, bookingId || null}); cancel → "cancel_booking" (bookingId || null, {reason || null}). Then
/// "schedule_status"/"cancel_status" and their switches: success with BOOKED/RESCHEDULED (CANCELED), or any failure, go to
/// "result_formatter" (model Ai / FormatterModel) and its text is sent; a success with another code goes nowhere.</item>
/// <item>handoff → "handoff_to_human" with reason || "The contact needs a team member."; the bot sends nothing else.</item>
/// </list>
/// An error answer of an action call (4xx/5xx) or of the reply ends the run like a failed n8n HTTP node: traced, nothing
/// more sent; a 409 from the reply (taken over, billing refused) is a quiet stop.
/// </summary>
public class BotEngine : IBotEngine
{
    public const string HandoffFallbackReason = "The contact needs a team member."; // text-ok: the workflow's default handoff reason, recorded on the event
    // What a LangChain agent answers when it runs out of iterations (n8n remembered it and the run then failed to parse).
    public const string MaxIterationsOutput = "Agent stopped due to max iterations."; // text-ok: memory only, never shown

    private static readonly IReadOnlySet<string> ScheduleSuccessCodes = new HashSet<string> { "BOOKED", "RESCHEDULED" };
    private static readonly IReadOnlySet<string> CancelSuccessCodes = new HashSet<string> { "CANCELED" };

    // The agent's decision as a strict JSON schema: the 7 fields of the prompt's output format, so the answer is always JSON.
    private static readonly AiJsonSchema DecisionSchema = new("bot_decision", (JsonObject)JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["action", "message", "scheduledStart", "confirmReplaceExisting", "bookingId", "serviceId", "reason"],
          "properties": {
            "action": { "type": "string" },
            "message": { "type": ["string", "null"] },
            "scheduledStart": { "type": ["string", "null"] },
            "confirmReplaceExisting": { "type": ["boolean", "null"] },
            "bookingId": { "type": ["string", "null"] },
            "serviceId": { "type": ["string", "null"] },
            "reason": { "type": ["string", "null"] }
          }
        }
        """)!);

    // The agent's tools (the n8n tool nodes): the bot capability that offers each one (null = every bot), the /api/ai tool it
    // runs, and how the AI's arguments ($fromAI) and the workflow's fixed values become that tool's call.
    private static readonly IReadOnlyList<AgentTool> Tools =
    [
        new("search_knowledge", BotCapability.Knowledge, BotTools.SearchKnowledge,
            (t, trigger, run, args, ct) => t.SearchKnowledgeAsync(run, new KnowledgeSearchRequest(trigger.ClientId, Arg(args, "query") ?? "", 5), ct)),
        new("get_services", BotCapability.Services, BotTools.Services,
            (t, trigger, run, _, ct) => t.GetServicesAsync(run, trigger.ClientId, ct)),
        new("get_available_slots", BotCapability.Availability, BotTools.AvailableSlots, AvailableSlotsAsync),
        new("get_my_bookings", BotCapability.Bookings, BotTools.MyBookings,
            (t, trigger, run, _, ct) => t.GetMyBookingsAsync(run, trigger.ClientId, trigger.ContactId ?? Guid.Empty, ct)),
        new("update_contact", null, BotTools.UpdateContact,
            (t, trigger, run, args, ct) => t.UpdateContactAsync(run, trigger.ClientId, trigger.ContactId ?? Guid.Empty,
                new UpdateContactContextRequest(null, Arg(args, "language"), null, null, null, null, null), ct)),
        new("update_contact_details", BotCapability.ContactUpdate, BotTools.UpdateContact,
            (t, trigger, run, args, ct) => t.UpdateContactAsync(run, trigger.ClientId, trigger.ContactId ?? Guid.Empty,
                new UpdateContactContextRequest(null, null, Arg(args, "city") ?? "", Arg(args, "timeline") ?? "", Arg(args, "notes") ?? "", null,
                    Arg(args, "qualification") ?? ""), ct)),
        new("update_stage", BotCapability.Pipeline, BotTools.UpdateContact,
            (t, trigger, run, args, ct) => t.UpdateContactAsync(run, trigger.ClientId, trigger.ContactId ?? Guid.Empty,
                new UpdateContactContextRequest(null, null, null, null, null, null, null, Arg(args, "stage")), ct))
    ];

    private readonly IAiModelClient _model;
    private readonly IBotToolService _tools;
    private readonly IBotMemoryRepository _memory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;
    private readonly IMessageTracer _tracer;
    private readonly ILogger<BotEngine> _logger;

    public BotEngine(IAiModelClient model, IBotToolService tools, IBotMemoryRepository memory, IUnitOfWork unitOfWork,
        IConfigManager config, IMessageTracer tracer, ILogger<BotEngine> logger)
    {
        _model = model;
        _tools = tools;
        _memory = memory;
        _unitOfWork = unitOfWork;
        _config = config;
        _tracer = tracer;
        _logger = logger;
    }

    public async Task RunAsync(AiTriggerPayload trigger, CancellationToken ct = default)
    {
        var test = trigger.TestSessionId is not null;
        var run = new Run(trigger, trigger.Bot!,
            new TraceIds(trigger.ClientId, test ? null : trigger.ConversationId, trigger.MessageId, trigger.Channel),
            new BotToolCall(trigger.ClientId, trigger.Bot?.Id, trigger.TestSessionId, test ? null : trigger.ConversationId, trigger.ContactId),
            DateTimeOffset.UtcNow);
        try
        {
            if (trigger.Bot is null) throw new ArgumentException("A bot run needs the trigger's bot."); // text-ok: programming error
            _tracer.Step(TraceSteps.BotRunStarted, TraceOutcomes.Ok, run.Ids, detail: new { botId = trigger.Bot.Id, test });

            var output = await RunAgentAsync(run, ct);
            var decision = BotDecision.TryParse(output);
            if (decision is null)
            {
                var reason = output == MaxIterationsOutput ? "max_tool_iterations" : "unreadable_output";
                _tracer.Step(TraceSteps.BotDecision, TraceOutcomes.Failed, run.Ids, detail: new { reason });
                return;
            }

            switch (decision.Action)
            {
                case BotDecision.Reply:
                    Decided(run, decision);
                    await ReplyAsync(run, decision.Message, ct);
                    break;
                case BotDecision.Schedule:
                    Decided(run, decision);
                    await ScheduleAsync(run, decision, ct);
                    break;
                case BotDecision.Cancel:
                    Decided(run, decision);
                    await CancelAsync(run, decision, ct);
                    break;
                case BotDecision.Handoff:
                    Decided(run, decision);
                    await HandoffAsync(run, decision, ct);
                    break;
                default:
                    _tracer.Step(TraceSteps.BotDecision, TraceOutcomes.Failed, run.Ids, detail: new { reason = "unknown_action", action = decision.Action });
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Bot run failed for conversation {ConversationId}.", trigger.ConversationId);
            _tracer.Step(TraceSteps.BotRunFailed, TraceOutcomes.Failed, run.Ids, detail: new { error = TraceText.Error(ex) });
        }
    }

    private void Decided(Run run, BotDecision decision) =>
        _tracer.Step(TraceSteps.BotDecision, TraceOutcomes.Ok, run.Ids, detail: new { action = decision.Action });

    // ---------------------------------------------------------------- AI Agent

    /// <summary>The agent's final output (raw text), after remembering the exchange.</summary>
    private async Task<string> RunAgentAsync(Run run, CancellationToken ct)
    {
        var trigger = run.Trigger;
        var test = trigger.TestSessionId is not null;
        var exchanges = _config.AiMemoryExchanges;

        var input = new List<AiInputItem>();
        foreach (var exchange in await _memory.ListRecentReadOnlyAsync(trigger.ConversationId, test, exchanges, ct))
        {
            input.Add(AiInputItem.User(exchange.UserText));
            input.Add(AiInputItem.Assistant(exchange.Output));
        }
        input.Add(AiInputItem.User(trigger.MessageText ?? ""));

        var offered = Tools.Where(t => t.Capability is null || run.Bot.Capabilities.Contains(t.Capability)).ToList();
        var specs = offered.Select(t => new AiToolSpec(t.Name, AiPromptTemplate.Load(AiPrompts.ToolDescription(t.Name)),
            JsonNode.Parse(AiPromptTemplate.Load(AiPrompts.ToolParameters(t.Name)))!.AsObject())).ToList();
        var system = AiPromptTemplate.RenderFile(AiPrompts.AgentSystem, AiPromptValues.ForMessage(trigger, run.Now));

        string? output = null;
        var iterations = _config.AiMaxToolIterations;
        for (var iteration = 1; iteration <= iterations && output is null; iteration++)
        {
            var response = await CallModelAsync(run, "agent", iteration,
                new AiModelRequest(_config.AiBotModel, system, input.ToList(), specs, DecisionSchema), ct);
            if (!response.WantsTools)
            {
                output = response.OutputText ?? "";
                break;
            }

            input.AddRange(response.OutputItems);
            foreach (var call in response.FunctionCalls)
            {
                input.Add(AiInputItem.FunctionOutput(call.CallId, await RunToolAsync(run, offered, call, ct)));
            }
        }
        output ??= MaxIterationsOutput;

        _memory.Add(new BotMemory
        {
            Id = Guid.NewGuid(),
            ClientId = trigger.ClientId,
            ConversationId = test ? null : trigger.ConversationId,
            TestSessionId = trigger.TestSessionId,
            UserText = trigger.MessageText ?? "",
            Output = output,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _unitOfWork.SaveChangesAsync(ct);
        await _memory.DeleteAllButNewestAsync(trigger.ConversationId, test, exchanges, ct);
        return output;
    }

    /// <summary>One tool call of the agent; the text the model reads back. Like an n8n tool node, a failure is answered to the
    /// model (which carries on), never ends the run.</summary>
    private async Task<string> RunToolAsync(Run run, IReadOnlyList<AgentTool> offered, AiFunctionCall call, CancellationToken ct)
    {
        var tool = offered.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null) return JsJson.Stringify(new { error = $"Unknown tool {call.Name}." }); // text-ok: read by the AI only

        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return JsJson.Stringify(new { error = "The tool arguments are not valid JSON." }); // text-ok: read by the AI only
        }

        try
        {
            var result = await _tools.CallAsync(run.Call, tool.Definition, (botRun, c) => tool.Execute(_tools, run.Trigger, botRun, args, c), ct);
            return result.IsError ? JsJson.Stringify(new { status = result.Status, error = result.Body }) : JsJson.Stringify(result.Body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Bot tool {Tool} failed.", call.Name);
            return JsJson.Stringify(new { status = 500, error = "The tool failed." }); // text-ok: read by the AI only
        }
    }

    private static Task<BotToolResult> AvailableSlotsAsync(IBotToolService tools, AiTriggerPayload trigger, BotRunContext run, JsonObject args, CancellationToken ct)
    {
        // The workflow sent the AI's date as the query parameter: empty = none, anything else must be a date (else 400).
        var text = Arg(args, "date");
        DateOnly? date = null;
        if (!string.IsNullOrEmpty(text))
        {
            if (!DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Task.FromResult(BotToolResult.BadRequest(new { error = $"The value '{text}' is not a valid date." })); // text-ok: read by the AI only
            }
            date = parsed;
        }
        return tools.GetAvailableSlotsAsync(run, trigger.ClientId, trigger.ContactId, null, date, null, ct);
    }

    // ---------------------------------------------------------------- actions

    private async Task ScheduleAsync(Run run, BotDecision decision, CancellationToken ct)
    {
        // The schedule_booking body; a value the endpoint couldn't bind (no or a bad start, a bad id) failed the n8n node.
        if (!TryStart(decision.ScheduledStart, out var start) || !TryId(decision.ServiceId, out var serviceId)
            || !TryId(decision.BookingId, out var bookingId) || !TryFlag(decision.ConfirmReplaceExisting, out var confirm)
            || run.Trigger.ContactId is not { } contactId)
        {
            _tracer.Step(TraceSteps.BotActionResult, TraceOutcomes.Failed, run.Ids, detail: new { action = BotDecision.Schedule, reason = "invalid_request" });
            return;
        }

        var request = new ScheduleBookingRequest(start, serviceId, null, "", OrNull(decision.Reason), confirm, bookingId);
        var result = await _tools.CallAsync(run.Call, BotTools.ScheduleBooking,
            (botRun, c) => _tools.ScheduleBookingAsync(botRun, run.Trigger.ClientId, contactId, request, c), ct);
        await FormatAndReplyAsync(run, BotDecision.Schedule, result, ScheduleSuccessCodes, ct);
    }

    private async Task CancelAsync(Run run, BotDecision decision, CancellationToken ct)
    {
        if (!TryId(decision.BookingId, out var bookingId) || run.Trigger.ContactId is not { } contactId)
        {
            _tracer.Step(TraceSteps.BotActionResult, TraceOutcomes.Failed, run.Ids, detail: new { action = BotDecision.Cancel, reason = "invalid_request" });
            return;
        }

        var result = await _tools.CallAsync(run.Call, BotTools.CancelBooking,
            (botRun, c) => _tools.CancelBookingAsync(botRun, run.Trigger.ClientId, contactId, bookingId, new CancelBookingRequest(OrNull(decision.Reason)), c), ct);
        await FormatAndReplyAsync(run, BotDecision.Cancel, result, CancelSuccessCodes, ct);
    }

    private async Task HandoffAsync(Run run, BotDecision decision, CancellationToken ct)
    {
        var reason = OrNull(decision.Reason) ?? HandoffFallbackReason;
        var result = await _tools.CallAsync(run.Call, BotTools.Handoff,
            (botRun, c) => _tools.HandoffAsync(botRun, run.Trigger.ConversationId, run.Trigger.ClientId, new HandoffToHumanRequest(reason), c), ct);
        _tracer.Step(TraceSteps.BotActionResult, result.IsError ? TraceOutcomes.Failed : TraceOutcomes.Ok, run.Ids,
            detail: new { action = BotDecision.Handoff, status = result.Status });
    }

    /// <summary>schedule_status / cancel_status and their switches: what reaches result_formatter, then send_message.</summary>
    private async Task FormatAndReplyAsync(Run run, string action, BotToolResult result, IReadOnlySet<string> successCodes, CancellationToken ct)
    {
        if (result.IsError)
        {
            _tracer.Step(TraceSteps.BotActionResult, TraceOutcomes.Failed, run.Ids, detail: new { action, status = result.Status });
            return;
        }

        var body = JsonSerializer.SerializeToNode(result.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)) as JsonObject;
        var success = body?["success"] is JsonValue s && s.TryGetValue<bool>(out var ok) && ok;
        var code = body?["code"] is JsonValue c && c.TryGetValue<string>(out var text) ? text : null;
        var formatted = !success || (code is not null && successCodes.Contains(code));
        _tracer.Step(TraceSteps.BotActionResult, formatted ? TraceOutcomes.Ok : TraceOutcomes.Skipped, run.Ids,
            detail: new { action, status = result.Status, success, code });
        if (!formatted) return;

        var values = AiPromptValues.ForFormatter(run.Trigger, run.Now, result.Body);
        var response = await CallModelAsync(run, "formatter", 1, new AiModelRequest(_config.AiFormatterModel,
            AiPromptTemplate.RenderFile(AiPrompts.FormatterSystem, values),
            [AiInputItem.User(AiPromptTemplate.RenderFile(AiPrompts.FormatterUser, values))]), ct);
        await ReplyAsync(run, response.OutputText ?? "", ct);
    }

    /// <summary>send_message: the bot's reply. A 409 (taken over, billing refused) stops quietly; another error fails.</summary>
    private async Task ReplyAsync(Run run, string? content, CancellationToken ct)
    {
        var result = await _tools.CallAsync(run.Call, BotTools.Reply,
            (botRun, c) => _tools.ReplyAsync(botRun, run.Trigger.ConversationId, run.Trigger.ClientId, new AiReplyRequest(content), c), ct);
        if (result.Status == StatusCodes.Status409Conflict)
        {
            var code = (JsonSerializer.SerializeToNode(result.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web)) as JsonObject)?["code"]?.GetValue<string>();
            _tracer.Step(TraceSteps.BotReplyStopped, TraceOutcomes.Skipped, run.Ids, detail: new { code });
            return;
        }
        _tracer.Step(TraceSteps.BotActionResult, result.IsError ? TraceOutcomes.Failed : TraceOutcomes.Ok, run.Ids,
            detail: new { action = BotDecision.Reply, status = result.Status });
    }

    private async Task<AiModelResponse> CallModelAsync(Run run, string role, int iteration, AiModelRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _model.CreateResponseAsync(request, ct);
            _tracer.Step(TraceSteps.BotModelCalled, TraceOutcomes.Ok, run.Ids, response.DurationMs, new
            {
                role, iteration, model = request.Model, status = response.Status, toolCalls = response.FunctionCalls.Select(f => f.Name).ToList(),
                inputTokens = response.Usage.InputTokens, outputTokens = response.Usage.OutputTokens, cachedTokens = response.Usage.CachedTokens,
                reasoningTokens = response.Usage.ReasoningTokens
            });
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _tracer.Step(TraceSteps.BotModelCalled, TraceOutcomes.Failed, run.Ids, detail: new { role, iteration, model = request.Model, error = TraceText.Error(ex) });
            throw;
        }
    }

    // ---------------------------------------------------------------- the workflow's value handling

    private static string? Arg(JsonObject args, string name) => args[name] switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var text) => text,
        var other => other.ToJsonString()
    };

    // JavaScript's `value || null` for a text.
    private static string? OrNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    // ScheduleBookingRequest.ScheduledStart as the endpoint bound it from JSON (ISO 8601; null or anything else = 400).
    private static bool TryStart(string? value, out DateTimeOffset start)
    {
        start = default;
        if (value is null) return false;
        try
        {
            start = JsonSerializer.Deserialize<DateTimeOffset>(JsonSerializer.Serialize(value));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // `value || null` then LenientNullableGuidConverter: empty = none, else it must be an id.
    private static bool TryId(string? value, out Guid? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Guid.TryParse(value, out var parsed)) return false;
        id = parsed;
        return true;
    }

    // LenientBoolConverter on the Set node's text: empty or none = false, else true/false.
    private static bool TryFlag(string? value, out bool flag)
    {
        flag = false;
        return string.IsNullOrWhiteSpace(value) || bool.TryParse(value, out flag);
    }

    private sealed record Run(AiTriggerPayload Trigger, AiBotConfig Bot, TraceIds Ids, BotToolCall Call, DateTimeOffset Now);

    private sealed record AgentTool(string Name, string? Capability, BotToolDefinition Definition,
        Func<IBotToolService, AiTriggerPayload, BotRunContext, JsonObject, CancellationToken, Task<BotToolResult>> Execute);
}
