namespace PlasticSurgery.Data.Entities;

/// <summary>One clinic's TikTok Login Kit connection — account authorization only (see
/// Services/ITikTokProviderClient.cs), NOT a messaging channel: unlike WhatsApp/Instagram/Facebook/Telegram
/// (ChannelIntegration), there is no Inbox/conversation traffic for TikTok in this phase, so this is its own
/// small table rather than a row in channel_integrations. One row per clinic (TikTok is a single provider,
/// unlike CalendarIntegration's one-row-per-clinic-per-provider). SculptFlow owns the whole OAuth2
/// relationship directly (connect, callback, token refresh, disconnect) — no n8n involvement, mirroring
/// CalendarIntegration's v2 architecture. MVP NOTE: tokens are stored in plain text, same caveat as
/// CalendarIntegration.AccessToken/ChannelIntegration.AccessToken — move to an encrypted column/secrets
/// manager before this handles real patient data at scale.</summary>
public class TikTokIntegration
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Status { get; set; } = TikTokIntegrationStatus.Disconnected;

    /// <summary>TikTok's stable per-app user id — the closest thing TikTok has to a permanent account id.</summary>
    public string? OpenId { get; set; }
    /// <summary>Only present if the app is part of a token/"union" group across the developer's own apps;
    /// otherwise null. Stored for completeness, not used for anything in this phase.</summary>
    public string? UnionId { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    /// <summary>TikTok refresh tokens themselves expire (~1 year) — once past this, reconnecting means a
    /// fresh authorization, not a refresh.</summary>
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    public bool IsHealthy { get; set; } = true;
    public string? LastProblemMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class TikTokIntegrationStatus
{
    public const string Disconnected = "disconnected";
    /// <summary>Redirected to TikTok's consent screen; waiting for the OAuth callback.</summary>
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string Error = "error";
}
