using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Providers.WhatsApp;

/// <summary>
/// The Meta Cloud API send calls: the only layer that knows WhatsApp's REST API. Everything above it
/// (WhatsAppService, MessageService, campaigns, n8n, the Inbox) only sees this interface.
///
///   MessageService / WhatsAppChannelSender -> IWhatsAppService -> IWhatsAppProvider (MetaWhatsAppProvider)
///
/// Implementations throw WhatsAppSendException (or another ChannelSendException) on failure and never log
/// credentials or message content.
/// </summary>
public interface IWhatsAppProvider
{
    /// <summary>True when the row carries everything needed to send (phone number id + access token). Status is
    /// checked separately by the caller.</summary>
    bool IsReady(ChannelIntegration integration);

    /// <summary>Sends free-form text. The 24h window is the caller's job. Returns Meta's message id,
    /// stored as Message.ExternalMessageId and matched against later status callbacks.</summary>
    Task<string> SendTextAsync(ChannelIntegration integration, string toPhone, string text, CancellationToken ct = default);

    /// <summary>Sends an approved template. Returns Meta's message id.</summary>
    Task<string> SendTemplateAsync(ChannelIntegration integration, string toPhone, WhatsAppTemplateSend template, CancellationToken ct = default);
}
