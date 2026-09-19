namespace PlasticSurgery.Dtos;

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
    /// <summary>When the last webhook delivery from the platform arrived (Telegram only today).</summary>
    DateTimeOffset? LastWebhookAt = null
);

/// <summary>Body for POST /api/channel-integrations/telegram/connect. The token is write-only: it is
/// validated with Telegram, stored server-side, and never returned or logged.</summary>
public record ConnectTelegramRequest(string? BotToken);

/// <summary>
/// Save/update request for one channel's connection config. AccessToken and
/// WebhookVerifyToken are optional on purpose — leave them null/empty to keep whatever value is
/// already stored (the dashboard form never round-trips a saved token back into an editable
/// field), and send a non-empty value only to set/replace it.
/// </summary>
public record SaveChannelIntegrationRequest(
    Guid ClinicId,
    string Channel,
    string? DisplayName,
    string? PhoneNumberId,
    string? WhatsAppBusinessId,
    string? PageId,
    string? InstagramBusinessId,
    string? AccessToken,
    string? WebhookVerifyToken,
    /// <summary>WhatsApp only — set when ConnectWhatsAppAsync auto-generates one during Embedded
    /// Signup. Null/omitted (the default) means "leave the stored PIN as-is", same convention as
    /// AccessToken/WebhookVerifyToken.</summary>
    string? Pin = null
);

/// <summary>
/// Sent by the browser after the WhatsApp login flow completes. Uses a full-page OAuth redirect
/// (Code + RedirectUri — RedirectUri must exactly match the URL used in the initial authorize
/// request) rather than the JS SDK popup, since the popup's code can't be exchanged server-side
/// (Meta associates it with an internal redirect_uri we have no way to reproduce). WabaId/
/// PhoneNumberId are optional — the full-page redirect has no popup to deliver them via
/// postMessage, so when omitted the backend auto-discovers them via the Graph API instead
/// (see ChannelIntegrationService.ConnectWhatsAppAsync).
/// </summary>
public record ConnectWhatsAppRequest(
    Guid ClinicId,
    string? Code = null,
    string? AccessToken = null,
    string? RedirectUri = null,
    string? WabaId = null,
    string? PhoneNumberId = null
);

/// <summary>
/// Sent by the browser after Facebook Login completes (Messenger use case). Uses the
/// fb:login-button plugin + FB.getLoginStatus(), which hands back an AccessToken directly
/// (implicit flow) rather than a Code — exactly one of the two should be set. Code is kept as an
/// option too in case a future caller uses the FB.login()/response_type:'code' pattern instead.
/// </summary>
public record ConnectFacebookRequest(
    Guid ClinicId,
    string? Code,
    string? AccessToken
);

/// <summary>DEBUG-ONLY: asks the backend to report a token's (or a code's) granted scopes.</summary>
public record DebugTokenRequest(string? Code = null, string? AccessToken = null, string? RedirectUri = null);
