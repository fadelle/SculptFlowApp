using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>
/// The bot editor's test conversation. A member's message runs the real AI workflow (n8n) with the bot's real instructions,
/// Knowledge Base and tools, and the reply is charged like any AI reply (ai_reply); write actions are simulated here
/// instead: the test contact's one booking lives on the session, contact changes and stage moves are only noted, nothing is
/// sent to a provider. A handoff ends the test (once, with the bot's handoff message) until it is reset.
/// </summary>
public interface IBotTestService
{
    /// <summary>Sends <paramref name="text"/> to the bot, starting a session when <paramref name="sessionId"/> is null (stale
    /// sessions are deleted then). Needs the plan's AI assistant (EntitlementDeniedException) and a bot that is ready
    /// (InvalidOperationException); a handed-off session refuses more messages (InvalidOperationException). Returns the
    /// session id; the reply arrives in the transcript.</summary>
    Task<Guid> SendAsync(Guid clientId, Guid botId, Guid? sessionId, string? text, CancellationToken ct = default);

    /// <summary>The session's lines (notes still as stored: BotTestMapper words them). Null when the session isn't this bot's.</summary>
    Task<(BotTestSession Session, IReadOnlyList<BotTestMessage> Lines)?> GetAsync(Guid clientId, Guid botId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Deletes the session (the panel starts a new one on the next message).</summary>
    Task ResetAsync(Guid clientId, Guid sessionId, CancellationToken ct = default);

    // ---- The workflow's calls for a test run (AI tool endpoints with a test session)

    /// <summary>The bot's reply: charged as ai_reply (no channel message), then added. False when billing refused it (noted).</summary>
    Task<bool> ReplyAsync(BotRunContext run, string? content, CancellationToken ct = default);

    /// <summary>The bot hands off: once per session (a repeated call changes nothing and returns false), with a note and the
    /// bot's handoff message.</summary>
    Task<bool> HandoffAsync(BotRunContext run, string? reason, CancellationToken ct = default);

    /// <summary>schedule_booking, simulated: checks the time against the real open slots and the test contact's simulated
    /// booking, and answers exactly like the real tool.</summary>
    Task<ScheduleBookingResult> ScheduleAsync(BotRunContext run, ScheduleBookingRequest request, CancellationToken ct = default);

    /// <summary>cancel_booking, simulated on the test contact's booking.</summary>
    Task<CancelBookingResult> CancelAsync(BotRunContext run, CancellationToken ct = default);

    /// <summary>get_my_bookings for the test contact (its simulated booking, if any).</summary>
    Task<UpcomingBookingsResponse> GetUpcomingAsync(BotRunContext run, CancellationToken ct = default);

    /// <summary>The test contact's booking context for get_available_slots.</summary>
    Task<ContactBookingContext> GetBookingContextAsync(BotRunContext run, CancellationToken ct = default);

    /// <summary>update_contact / details / stage, simulated: noted, nothing changes (a language-only update isn't noted).</summary>
    Task NoteContactUpdateAsync(BotRunContext run, UpdateContactContextRequest request, CancellationToken ct = default);
}
