namespace PlasticSurgery.Services;

/// <summary>
/// Sends outbound WhatsApp messages through the active provider (WhatsApp:Provider — Meta Cloud API or
/// Infobip, see IWhatsAppProvider), using whichever WhatsApp ChannelIntegration the clinic has connected
/// (Settings → Messaging Integrations). This is the one place that ever sends WhatsApp — MessageService is the only caller, so the
/// dashboard, template sends, campaigns, and (eventually) n8n all go through the same sending
/// logic instead of each reimplementing it.
/// </summary>
public interface IWhatsAppService
{
    /// <summary>Sends a free-form text message. Only allowed within the 24h customer service
    /// window — callers must check Conversation.IsServiceWindowOpen() first; this method itself
    /// doesn't know about conversations. Returns Meta's message id (stored as Message.ExternalMessageId).</summary>
    Task<string> SendTextMessageAsync(Guid clinicId, string toPhone, string text, CancellationToken ct = default);

    /// <summary>Sends an approved WhatsApp template message — the only outbound message type Meta
    /// allows outside the 24h customer service window. bodyParameters are positional {{1}}, {{2}}...
    /// substitutions for the template's body text, in order. Returns Meta's message id.</summary>
    Task<string> SendTemplateMessageAsync(
        Guid clinicId, string toPhone, string templateName, string languageCode,
        IReadOnlyList<string> bodyParameters, CancellationToken ct = default);
}
