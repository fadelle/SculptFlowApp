using System.Text.Json;
using System.Text.Json.Nodes;
using Omni.App.Common.Statics;

namespace Omni.App.Tests.Support;

/// <summary>A scripted AI model: answers each call with the next queued answer and keeps every request.</summary>
internal sealed class FakeAiModel : IAiModelClient
{
    private readonly Queue<Func<AiModelRequest, AiModelResponse>> _answers = new();
    private int _calls;

    public List<AiModelRequest> Requests { get; } = new();

    /// <summary>The agent's final decision (the 7 fields; missing ones null).</summary>
    public FakeAiModel Decide(string action, string? message = null, string? scheduledStart = null, bool? confirmReplaceExisting = false,
        string? bookingId = null, string? serviceId = null, string? reason = null) =>
        Text(JsonSerializer.Serialize(new { action, message, scheduledStart, confirmReplaceExisting, bookingId, serviceId, reason }));

    /// <summary>A final text answer (the agent's raw output or the formatter's message).</summary>
    public FakeAiModel Text(string text)
    {
        _answers.Enqueue(_ => Response(text, []));
        return this;
    }

    /// <summary>The model asks for tool calls: (name, arguments JSON).</summary>
    public FakeAiModel Call(params (string Name, string Arguments)[] calls)
    {
        _answers.Enqueue(_ => Response(null, calls.Select(c => new AiFunctionCall($"call_{Guid.NewGuid():N}", c.Name, c.Arguments)).ToList()));
        return this;
    }

    public FakeAiModel Throw(Exception ex)
    {
        _answers.Enqueue(_ => throw ex);
        return this;
    }

    /// <summary>Every call without a queued answer asks for this tool again.</summary>
    public bool KeepCallingTools { get; set; }

    public Task<AiModelResponse> CreateResponseAsync(AiModelRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        _calls++;
        if (_answers.Count == 0 && KeepCallingTools) return Task.FromResult(Response(null, [new AiFunctionCall($"call_{_calls}", "get_services", "{}")]));
        var answer = _answers.Count > 0 ? _answers.Dequeue() : throw new InvalidOperationException("FakeAiModel: no answer queued.");
        return Task.FromResult(answer(request));
    }

    private AiModelResponse Response(string? text, IReadOnlyList<AiFunctionCall> calls)
    {
        var items = calls.Select(c => new AiInputItem(new JsonObject
        {
            ["type"] = "function_call", ["call_id"] = c.CallId, ["name"] = c.Name, ["arguments"] = c.Arguments
        })).ToList();
        if (text is not null) items.Add(AiInputItem.Assistant(text));
        return new AiModelResponse($"resp_{_calls}", "completed", text, calls, items, new AiTokenUsage(100, 20, 120, 10, 5), 12.5);
    }
}

/// <summary>
/// IBotToolService for the engine: CallAsync checks the bot's capability (TOOL_NOT_ALLOWED) and the plan's bookings feature
/// (FEATURE_NOT_INCLUDED) like the real one and then runs the tool; each tool answers what was set for it and keeps its
/// arguments.
/// </summary>
internal sealed class FakeBotTools : IBotToolService
{
    public HashSet<string> Capabilities { get; set; } = new(BotCapability.All);
    public bool BookingsIncluded { get; set; } = true;
    public Dictionary<string, BotToolResult> Answers { get; } = new();
    public List<(string Tool, BotToolCall Call, object? Arguments)> Calls { get; } = new();
    public List<string> Routes { get; } = new();

    public async Task<BotToolResult> CallAsync(BotToolCall call, BotToolDefinition tool, Func<BotRunContext, CancellationToken, Task<BotToolResult>> body,
        CancellationToken ct = default)
    {
        Routes.Add(tool.Route);
        if (tool.Capability is { } capability && !Capabilities.Contains(capability)) return BotToolResult.Ok(BotToolOutcomes.NotAllowed(BotToolCheck.NotAllowed));
        if (tool.Feature is { } feature && !BookingsIncluded) return BotToolResult.Ok(BotToolOutcomes.FeatureNotIncluded(feature));
        var run = new BotRunContext(call.ClientId, call.BotId ?? Guid.Empty, Capabilities, null, null, call.TestSessionId);
        return await body(run, ct);
    }

    private Task<BotToolResult> Answer(string tool, BotRunContext run, object? arguments)
    {
        Calls.Add((tool, new BotToolCall(run.ClientId, run.BotId, run.TestSessionId), arguments));
        return Task.FromResult(Answers.TryGetValue(tool, out var answer) ? answer : BotToolResult.Ok(new { success = true, tool }));
    }

    public Task<BotToolResult> SearchKnowledgeAsync(BotRunContext run, KnowledgeSearchRequest request, CancellationToken ct = default) => Answer("search_knowledge", run, request);
    public Task<BotToolResult> GetClientInfoAsync(BotRunContext run, Guid clientId, CancellationToken ct = default) => Answer("client_info", run, clientId);
    public Task<BotToolResult> GetServicesAsync(BotRunContext run, Guid clientId, CancellationToken ct = default) => Answer("get_services", run, clientId);
    public Task<BotToolResult> GetContactContextAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default) => Answer("contact", run, contactId);
    public Task<BotToolResult> UpdateContactAsync(BotRunContext run, Guid clientId, Guid contactId, UpdateContactContextRequest request, CancellationToken ct = default) =>
        Answer("update_contact", run, (contactId, request));
    public Task<BotToolResult> GetAvailableSlotsAsync(BotRunContext run, Guid clientId, Guid? contactId, Guid? serviceId, DateOnly? date, int? days, CancellationToken ct = default) =>
        Answer("get_available_slots", run, (contactId, date));
    public Task<BotToolResult> ScheduleBookingAsync(BotRunContext run, Guid clientId, Guid contactId, ScheduleBookingRequest request, CancellationToken ct = default) =>
        Answer("schedule", run, (contactId, request));
    public Task<BotToolResult> GetMyBookingsAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default) => Answer("get_my_bookings", run, contactId);
    public Task<BotToolResult> CancelBookingAsync(BotRunContext run, Guid clientId, Guid contactId, Guid? bookingId, CancelBookingRequest request, CancellationToken ct = default) =>
        Answer("cancel", run, (contactId, bookingId, request));
    public Task<BotToolResult> ReplyAsync(BotRunContext run, Guid conversationId, Guid clientId, AiReplyRequest request, CancellationToken ct = default) =>
        Answer("reply", run, (conversationId, request.Content));
    public Task<BotToolResult> HandoffAsync(BotRunContext run, Guid conversationId, Guid clientId, HandoffToHumanRequest request, CancellationToken ct = default) =>
        Answer("handoff", run, (conversationId, request.Reason));
}

/// <summary>crm.bot_memory in memory, with the repository's newest-first window and trimming.</summary>
internal sealed class FakeBotMemory : IBotMemoryRepository
{
    public List<BotMemory> Rows { get; } = new();

    private IEnumerable<BotMemory> Of(Guid key, bool test) =>
        Rows.Where(m => test ? m.TestSessionId == key : m.ConversationId == key).OrderBy(m => m.CreatedAt);

    public Task<IReadOnlyList<BotMemory>> ListRecentReadOnlyAsync(Guid key, bool test, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BotMemory>>(take <= 0 ? [] : Of(key, test).TakeLast(take).ToList());

    public void Add(BotMemory memory) => Rows.Add(memory);

    public Task<int> DeleteAllButNewestAsync(Guid key, bool test, int keep, CancellationToken ct = default)
    {
        var old = Of(key, test).SkipLast(Math.Max(keep, 0)).ToList();
        foreach (var row in old) Rows.Remove(row);
        return Task.FromResult(old.Count);
    }
}
