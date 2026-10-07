using PlasticSurgery.Business.Contracts.Providers.Channels;
using PlasticSurgery.Business.Contracts.Services.Inbox;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Providers.Channels;

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
