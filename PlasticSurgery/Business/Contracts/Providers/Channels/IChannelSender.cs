using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Providers.Channels;

/// <summary>
/// One outbound "adapter" per messaging channel, resolved by Conversation.Channel. MessageService owns
/// everything channel-independent (mode checks, persistence, conversation/lead bookkeeping, SignalR,
/// events) and calls <see cref="SendTextAsync"/> for the one channel-specific step: actually
/// delivering free-form text to the external platform.
///
///   n8n / dashboard -> MessageService -> IChannelSender (by conversation.Channel) -> WhatsApp / Telegram
///
/// Implementations must throw BEFORE anything is persisted if the send can't happen (closed WhatsApp
/// window, missing address, provider rejection) — MessageService only writes the Message row after
/// this returns, so a failed send leaves no trace.
/// </summary>
public interface IChannelSender
{
    /// <summary>The ConversationChannel value this sender handles.</summary>
    string Channel { get; }

    /// <summary>Delivers plain text to the lead on this channel and returns the provider's external
    /// message id (stored as Message.ExternalMessageId, used for idempotency/status updates).
    /// The conversation's Lead is loaded.</summary>
    Task<string> SendTextAsync(Conversation conversation, string text, CancellationToken ct = default);
}
