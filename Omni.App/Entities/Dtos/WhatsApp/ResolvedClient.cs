namespace Omni.App.Entities.Dtos.WhatsApp;

/// <summary>The trusted client/connection a ParsedMetaEvent belongs to — resolved by
/// MetaWebhookProcessor from PhoneNumberId/WabaId against the client's own stored WhatsApp
/// connection (see IWhatsAppConnectionResolver), never taken from n8n or the Meta payload as an
/// identity claim.</summary>
public record ResolvedClient(Guid ClientId, Guid ChannelIntegrationId);
