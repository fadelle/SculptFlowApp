using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Business.Contracts.Services.Inbox;

/// <summary>A client's automatic replies (crm.auto_replies), one per channel Omni can send on: a reply to a contact's first
/// message in a conversation and one to a message that reopens a closed conversation (the same text, its own text, or none).
/// By default they go out only while the team answers the conversation; SendWhenAiAnswers also sends them next to the AI
/// bot. MessageService.IngestAsync sends them as automatic messages (billed like any other outbound message).</summary>
public interface IAutoReplyService
{
    /// <summary>Every channel Omni can send on, with the client's settings (defaults: off), whether it is connected and
    /// whether the AI bot answers there. Throws KeyNotFoundException when the client doesn't exist.</summary>
    Task<IReadOnlyList<AutoReplyChannelDto>> ListAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Saves one channel's settings (creating its row on first save). Throws ArgumentException for an unknown
    /// channel or mode, a missing or too long message; KeyNotFoundException when the client doesn't exist.</summary>
    Task SaveAsync(Guid clientId, SaveAutoReplyRequest request, CancellationToken ct = default);

    /// <summary>Saves several channels at once (the Auto-respond page's one Save), all or nothing: every request is checked
    /// first; an invalid one throws AutoReplyInvalidException naming its channel and nothing is saved.</summary>
    Task SaveAllAsync(Guid clientId, IReadOnlyList<SaveAutoReplyRequest> requests, CancellationToken ct = default);

    /// <summary>The reply a contact's message on <paramref name="channel"/> gets for <paramref name="trigger"/>
    /// (AutoReplyTrigger), or why none (AutoReplyRules.Skip*).</summary>
    Task<AutoReplyChoice> ChooseAsync(Guid clientId, string channel, string trigger, bool aiAnswers, CancellationToken ct = default);
}
