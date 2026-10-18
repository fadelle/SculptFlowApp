using Omni.App.Business.Contracts.HttpClients.Telegram;
using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Engines.Telegram;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Providers.Channels;

/// <summary>
/// Telegram's implementation of the shared outbound seam (see IChannelSender). MessageService has
/// already checked the conversation belongs to the client and (for AI sends) re-checked mode == ai;
/// this only does the Telegram-specific part: find THIS client's connected bot, send to the
/// conversation's chat id, and return the external message id in the same "chatId:messageId" form the
/// inbound side stores.
///
/// No 24-hour window, no phone number, no templates — a Telegram bot can message any user who has
/// started a chat with it, any time.
/// </summary>
public class TelegramChannelSender : IChannelSender
{
    private readonly IChannelIntegrationRepository _integrations;
    private readonly ITelegramBotClient _client;

    public TelegramChannelSender(IChannelIntegrationRepository integrations, ITelegramBotClient client)
    {
        _integrations = integrations;
        _client = client;
    }

    public string Channel => ConversationChannel.Telegram;

    public async Task<string> SendTextAsync(Conversation conversation, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversation.ExternalThreadId))
        {
            throw new InvalidOperationException("inbox.errors.telegramNoChat");
        }

        // Client-scoped lookup: the token used is always the one belonging to the conversation's own client.
        var integration = await _integrations.GetReadOnlyAsync(conversation.ClientId, ChannelType.Telegram, ct);

        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected || string.IsNullOrEmpty(integration.AccessToken))
        {
            throw new InvalidOperationException("channels.telegram.errors.notConnected");
        }

        var messageId = await _client.SendMessageAsync(integration.AccessToken, conversation.ExternalThreadId, text, ct);
        return TelegramWebhookProcessor.ExternalMessageId(conversation.ExternalThreadId, messageId);
    }
}
