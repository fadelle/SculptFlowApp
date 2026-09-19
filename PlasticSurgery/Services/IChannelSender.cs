using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

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

/// <summary>Base for a failed delivery attempt to an external messaging platform — controllers map
/// it to 502 Bad Gateway. WhatsApp and Telegram failures both derive from it.</summary>
public class ChannelSendException : Exception
{
    public ChannelSendException(string message) : base(message) { }
}

/// <summary>WhatsApp's free-form text send, with the WhatsApp-only rules (24-hour customer service
/// window, phone number required). Moved here verbatim from MessageService so the shared send flow
/// no longer has WhatsApp hard-wired into it; behavior and error messages are unchanged.</summary>
public class WhatsAppChannelSender : IChannelSender
{
    private readonly IWhatsAppService _whatsApp;

    public WhatsAppChannelSender(IWhatsAppService whatsApp)
    {
        _whatsApp = whatsApp;
    }

    public string Channel => ConversationChannel.WhatsApp;

    public async Task<string> SendTextAsync(Conversation conversation, string text, CancellationToken ct = default)
    {
        // WhatsApp only allows free-form messages within 24h of the customer's last message —
        // enforced here server-side, not just by hiding the composer in the UI (a stale page, a
        // direct API call, or a race with the window expiring must all be caught too).
        if (!conversation.IsServiceWindowOpen(DateTimeOffset.UtcNow))
        {
            throw new ServiceWindowClosedException(
                "The 24-hour WhatsApp customer service window is closed for this conversation — send an approved template instead.");
        }

        var toPhone = conversation.Lead?.Phone
            ?? throw new InvalidOperationException("This lead has no phone number on file — can't send a WhatsApp message.");

        return await _whatsApp.SendTextMessageAsync(conversation.ClinicId, toPhone, text, ct);
    }
}
