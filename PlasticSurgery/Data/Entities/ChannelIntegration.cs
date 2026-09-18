namespace PlasticSurgery.Data.Entities;

/// <summary>
/// A clinic's connection config for one messaging channel (WhatsApp Business API, Instagram,
/// or Facebook Messenger via the Meta Graph API). Lives under Settings → Channels &amp;
/// Integrations in the dashboard — see Pages/Settings/Integrations.cshtml.
///
/// MVP NOTE: AccessToken and WebhookVerifyToken are stored as plain text, matching the rest of
/// this MVP (no auth yet — see the plan's Compliance section). Move these to an encrypted column
/// or a secrets manager (e.g. Supabase Vault, Azure Key Vault) before connecting a real WhatsApp/
/// Meta app or handling real patient conversations.
/// </summary>
public class ChannelIntegration
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }

    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = ChannelIntegrationStatus.Disconnected;

    /// <summary>Human-friendly label for the connected account (phone number, Page name, etc.) — display only.</summary>
    public string? DisplayName { get; set; }

    // WhatsApp Business API (Cloud API)
    public string? PhoneNumberId { get; set; }
    public string? WhatsAppBusinessId { get; set; }

    // Facebook / Instagram (Meta Graph API)
    public string? PageId { get; set; }
    public string? InstagramBusinessId { get; set; }

    // Shared
    public string? AccessToken { get; set; }
    public string? WebhookVerifyToken { get; set; }

    /// <summary>WhatsApp only — the 6-digit two-step-verification PIN Meta requires to register a
    /// phone number for Cloud API use (POST /{phone-number-id}/register). Auto-generated the first
    /// time a number is connected via Embedded Signup — see ChannelIntegrationService.ConnectWhatsAppAsync.
    /// Not a bearer credential (can't authenticate API calls on its own), so it's shown in the
    /// dashboard rather than hidden like AccessToken — staff may need it for WhatsApp Business App
    /// coexistence login.</summary>
    public string? Pin { get; set; }

    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string? LastError { get; set; }

    // ---------------------------------------------------------------
    // WhatsApp account/phone health — populated from Meta's account_update, account_review_update,
    // phone_number_quality_update and phone_number_name_update webhooks (via n8n → the AI/WhatsApp
    // integration events endpoint). See WhatsAppHealthService and Pages/WhatsApp/Health.cshtml.
    // Deliberately free text (not CHECK-constrained) — Meta's own status vocabularies for these
    // aren't guaranteed stable, and "tolerate unknown values, keep raw metadata" is the explicit
    // requirement here (see WhatsAppHealthEvent.RawMetadataJson for the raw payload).
    // ---------------------------------------------------------------

    /// <summary>Meta Business Manager id that owns the WABA — distinct from WhatsAppBusinessId
    /// (the WABA itself).</summary>
    public string? MetaBusinessId { get; set; }
    /// <summary>The WhatsApp-verified business name for this number (separate from DisplayName,
    /// which mixes in the phone number for other channels too).</summary>
    public string? VerifiedName { get; set; }

    public string? AccountStatus { get; set; }
    public string? AccountReviewStatus { get; set; }
    public string? PhoneQualityRating { get; set; }
    public string? PhoneStatus { get; set; }
    public string? NameStatus { get; set; }

    public bool IsHealthy { get; set; } = true;
    /// <summary>One of WhatsAppHealthLevel — the simplified state the dashboard actually shows;
    /// derived from the raw fields above rather than shown directly to staff.</summary>
    public string? HealthLevel { get; set; }

    public string? LastProblemCode { get; set; }
    public string? LastProblemMessage { get; set; }

    public DateTimeOffset? LastWebhookAt { get; set; }
    public DateTimeOffset? LastHealthEventAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Clinic? Clinic { get; set; }
}

/// <summary>Simplified health states shown in the dashboard — derived from the raw account/phone
/// status fields on ChannelIntegration, never Meta's raw vocabulary directly.</summary>
public static class WhatsAppHealthLevel
{
    public const string Healthy = "healthy";
    public const string Warning = "warning";
    public const string Problem = "problem";
    public const string Disconnected = "disconnected";
    public const string Unknown = "unknown";
}

/// <summary>Allowed values for ChannelIntegration.Channel — must match schema.sql's CHECK constraint.</summary>
public static class ChannelType
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Facebook = "facebook";

    public static readonly IReadOnlyList<string> All = new[] { WhatsApp, Instagram, Facebook };
}

/// <summary>Allowed values for ChannelIntegration.Status — must match schema.sql's CHECK constraint.</summary>
public static class ChannelIntegrationStatus
{
    public const string Disconnected = "disconnected";
    public const string Connected = "connected";
    public const string Error = "error";
}
