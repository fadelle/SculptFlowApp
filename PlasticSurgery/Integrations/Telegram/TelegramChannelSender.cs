using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Telegram;

/// <summary>
/// Telegram's implementation of the shared outbound seam (see IChannelSender). MessageService has
/// already checked the conversation belongs to the clinic and (for AI sends) re-checked mode == ai;
/// this only does the Telegram-specific part: find THIS clinic's connected bot, send to the
/// conversation's chat id, and return the external message id in the same "chatId:messageId" form the
/// inbound side stores.
///
/// No 24-hour window, no phone number, no templates — a Telegram bot can message any user who has
/// started a chat with it, any time.
/// </summary>
public class TelegramChannelSender : IChannelSender
{
    private readonly ApplicationDbContext _db;
    private readonly ITelegramBotClient _client;

    public TelegramChannelSender(ApplicationDbContext db, ITelegramBotClient client)
    {
        _db = db;
        _client = client;
    }

    public string Channel => ConversationChannel.Telegram;

    public async Task<string> SendTextAsync(Conversation conversation, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversation.ExternalThreadId))
        {
            throw new InvalidOperationException("This Telegram conversation has no chat id on file — can't send.");
        }

        // Clinic-scoped lookup: the token used is always the one belonging to the conversation's own clinic.
        var integration = await _db.ChannelIntegrations.AsNoTracking().FirstOrDefaultAsync(
            c => c.ClinicId == conversation.ClinicId && c.Channel == ChannelType.Telegram, ct);

        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || string.IsNullOrEmpty(integration.AccessToken))
        {
            throw new InvalidOperationException(
                "This clinic doesn't have a connected Telegram bot — reconnect it in Settings → Channels & Integrations.");
        }

        var messageId = await _client.SendMessageAsync(integration.AccessToken, conversation.ExternalThreadId, text, ct);
        return TelegramWebhookProcessor.ExternalMessageId(conversation.ExternalThreadId, messageId);
    }
}
