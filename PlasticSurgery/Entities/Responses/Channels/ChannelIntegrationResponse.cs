using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Responses.Channels;

public record ChannelIntegrationResponse(
    Guid Id,
    Guid ClinicId,
    string Channel,
    string Status,
    string? DisplayName,
    string? PhoneNumberId,
    string? WhatsAppBusinessId,
    string? PageId,
    string? InstagramBusinessId,
    bool HasAccessToken,
    bool HasWebhookVerifyToken,
    DateTimeOffset? LastVerifiedAt,
    string? LastError,
    DateTimeOffset UpdatedAt,
    /// <summary>WhatsApp only — the two-step-verification PIN registered with Meta for this phone
    /// number. Not a bearer credential, so unlike AccessToken it's shown directly rather than
    /// hidden — staff may need it for WhatsApp Business App coexistence login.</summary>
    string? Pin = null,
    /// <summary>Telegram only. The bot token and webhook secret are NEVER part of any response —
    /// HasAccessToken / HasWebhookVerifyToken are the only trace of them.</summary>
    string? TelegramBotId = null,
    string? TelegramBotUsername = null,
    /// <summary>One of WebhookStatus (active / pending / error / not_registered); null for channels without a self-registered webhook.</summary>
    string? WebhookStatus = null,
    DateTimeOffset? WebhookRegisteredAt = null,
    /// <summary>When the last webhook delivery from the platform arrived (Telegram and Infobip WhatsApp).</summary>
    DateTimeOffset? LastWebhookAt = null,
    /// <summary>WhatsApp only — which provider this connection goes through (ChannelProvider: meta / infobip).
    /// Server-side only (Razor pages): never serialized, so the browser can't see which provider we use.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore] string? Provider = null,
    /// <summary>Infobip only — the business sender number, digits only. Server-side only, like Provider. The
    /// webhook secret is never part of this record at all.</summary>
    [property: System.Text.Json.Serialization.JsonIgnore] string? ProviderSenderId = null
);
