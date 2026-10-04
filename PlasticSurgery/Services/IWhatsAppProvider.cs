using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>An approved template to send — provider-neutral. BodyParameters are the positional {{1}}, {{2}}...
/// values for the template body, in order. Header/button parameters and media get added here when needed.</summary>
public record WhatsAppTemplateSend(string Name, string LanguageCode, IReadOnlyList<string> BodyParameters);

/// <summary>
/// One WhatsApp Business Solution Provider: the only layer that knows a provider's REST API. Everything above
/// it (WhatsAppService, MessageService, campaigns, n8n, the Inbox) is provider-independent.
///
///   MessageService / WhatsAppChannelSender -> IWhatsAppService (picks the active provider) -> IWhatsAppProvider
///     meta    -> Integrations/WhatsApp/MetaWhatsAppProvider.cs  (Meta Cloud API, per-clinic token)
///     infobip -> Integrations/Infobip/InfobipWhatsAppProvider.cs (SculptFlow's Infobip account, per-clinic sender)
///
/// Which one runs is the global WhatsApp:Provider setting. Inbound is the mirror image: each provider has its
/// own webhook controller + parser that turns its payload into ParsedMetaEvent and reuses the same handlers.
/// Implementations throw WhatsAppSendException (or another ChannelSendException) on failure and never log
/// credentials or message content.
/// </summary>
public interface IWhatsAppProvider
{
    /// <summary>One of <see cref="ChannelProvider"/>.</summary>
    string Name { get; }

    /// <summary>True when the row carries everything this provider needs to send (Meta: phone number id +
    /// access token; Infobip: the sender number). Status is checked separately by the caller.</summary>
    bool IsReady(ChannelIntegration integration);

    /// <summary>Sends free-form text. The 24h window is the caller's job. Returns the provider's message id,
    /// stored as Message.ExternalMessageId and matched against later status callbacks.</summary>
    Task<string> SendTextAsync(ChannelIntegration integration, string toPhone, string text, CancellationToken ct = default);

    /// <summary>Sends an approved template. Returns the provider's message id.</summary>
    Task<string> SendTemplateAsync(ChannelIntegration integration, string toPhone, WhatsAppTemplateSend template, CancellationToken ct = default);
}
