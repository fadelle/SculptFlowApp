namespace PlasticSurgery.Entities.Requests.Channels;

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
