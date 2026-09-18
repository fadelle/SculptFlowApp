namespace PlasticSurgery.Integrations.WhatsApp.Models;

/// <summary>The trusted clinic/connection a ParsedMetaEvent belongs to — resolved by
/// MetaWebhookProcessor from PhoneNumberId/WabaId against the clinic's own stored WhatsApp
/// connection (see IWhatsAppConnectionResolver), never taken from n8n or the Meta payload as an
/// identity claim.</summary>
public record ResolvedClinic(Guid ClinicId, Guid ChannelIntegrationId);
