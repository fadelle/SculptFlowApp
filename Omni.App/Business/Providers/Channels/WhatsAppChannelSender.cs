using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Providers.Channels;

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
            throw new ServiceWindowClosedException("inbox.errors.windowClosed");
        }

        var toPhone = conversation.Contact?.Phone
            ?? throw new InvalidOperationException("inbox.errors.noPhone");

        return await _whatsApp.SendTextMessageAsync(conversation.ClientId, toPhone, text, ct);
    }
}
