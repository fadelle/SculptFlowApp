using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Requests.Knowledge;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>
/// The AI bot's tools: one method per tool, each answering what its /api/ai endpoint answers (status + the JSON the AI
/// reads), all delegating to the services the dashboard uses. A test run (BotRunContext.IsTest) reads real data but
/// simulates every write (IBotTestService).
/// <para>The tool methods take an already authorized run. <see cref="CallAsync"/> is the whole call around one: the bot's
/// check (IBotRuntimeService.AuthorizeAsync, TOOL_NOT_ALLOWED), the plan's feature (FEATURE_NOT_INCLUDED), the tool, and a
/// bot.tool_called trace step. On /api/ai the BotTool, RequireFeature and TraceBotTool filters do those same steps around
/// the action with the same outcomes (BotToolOutcomes).</para>
/// </summary>
public interface IBotToolService
{
    /// <summary>Checks the call for <paramref name="tool"/> (bot, capability, test session, then the plan's feature), runs
    /// <paramref name="body"/> with the run when allowed, and traces a bot.tool_called step (failed for a 4xx/5xx answer
    /// or an exception, which is rethrown).</summary>
    Task<BotToolResult> CallAsync(BotToolCall call, BotToolDefinition tool,
        Func<BotRunContext, CancellationToken, Task<BotToolResult>> body, CancellationToken ct = default);

    /// <summary>search_knowledge: chunks of the client's approved Knowledge Base, only the bot's sources. 400 without a
    /// client or query, 503 when the embeddings provider fails.</summary>
    Task<BotToolResult> SearchKnowledgeAsync(BotRunContext run, KnowledgeSearchRequest request, CancellationToken ct = default);

    /// <summary>get_client_info: hours, location, contact info, booking rules (404 for an unknown client).</summary>
    Task<BotToolResult> GetClientInfoAsync(BotRunContext run, Guid clientId, CancellationToken ct = default);

    /// <summary>get_services: the client's active services only.</summary>
    Task<BotToolResult> GetServicesAsync(BotRunContext run, Guid clientId, CancellationToken ct = default);

    /// <summary>get_contact_context: the contact (404 when not the client's); a test run's simulated contact.</summary>
    Task<BotToolResult> GetContactContextAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>update_contact / update_contact_details / update_stage: each field needs its capability
    /// (IBotRuntimeService.CheckContactUpdate, TOOL_NOT_ALLOWED otherwise); never identity fields.</summary>
    Task<BotToolResult> UpdateContactAsync(BotRunContext run, Guid clientId, Guid contactId, UpdateContactContextRequest request, CancellationToken ct = default);

    /// <summary>get_available_slots: open slots (days default 1 with a date, else 7) with the contact's booking context
    /// when <paramref name="contactId"/> is given (404 when not the client's) or the run is a test.</summary>
    Task<BotToolResult> GetAvailableSlotsAsync(BotRunContext run, Guid clientId, Guid? contactId, Guid? serviceId, DateOnly? date, int? days, CancellationToken ct = default);

    /// <summary>schedule_booking: book, or reschedule with consent; ScheduleBookingResult with the AI instruction.</summary>
    Task<BotToolResult> ScheduleBookingAsync(BotRunContext run, Guid clientId, Guid contactId, ScheduleBookingRequest request, CancellationToken ct = default);

    /// <summary>get_my_bookings: the contact's upcoming bookings.</summary>
    Task<BotToolResult> GetMyBookingsAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>cancel_booking: CancelBookingResult with the AI instruction.</summary>
    Task<BotToolResult> CancelBookingAsync(BotRunContext run, Guid clientId, Guid contactId, Guid? bookingId, CancelBookingRequest request, CancellationToken ct = default);

    /// <summary>reply: sends the bot's message. 404 when the conversation isn't the client's; 409 conversation_in_human_mode
    /// or ai_reply_refused (the workflow stops on these without retrying).</summary>
    Task<BotToolResult> ReplyAsync(BotRunContext run, Guid conversationId, Guid clientId, AiReplyRequest request, CancellationToken ct = default);

    /// <summary>handoff_to_human: { handedOff }, 404 when the conversation isn't the client's.</summary>
    Task<BotToolResult> HandoffAsync(BotRunContext run, Guid conversationId, Guid clientId, HandoffToHumanRequest request, CancellationToken ct = default);
}
