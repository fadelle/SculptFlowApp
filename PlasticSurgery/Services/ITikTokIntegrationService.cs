using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Settings → Channels &amp; Integrations' TikTok card (Login Kit account connection only — see
/// Data/Entities/TikTokIntegration.cs for why this isn't a messaging channel in this phase). SculptFlow owns
/// the whole OAuth2 relationship directly (see ITikTokProviderClient, TikTokOAuthController); n8n has no role
/// in any of this.
/// </summary>
public interface ITikTokIntegrationService
{
    /// <summary>The clinic's TikTok row, or a synthetic "disconnected" one (not persisted) if it never
    /// connected — same convention as IChannelIntegrationService.ListAsync/ICalendarIntegrationService.ListAsync.
    /// Opportunistically refreshes the access token first if it's connected and near/past expiry, so a
    /// revoked/expired connection is discovered (and surfaced as unhealthy) on the next page load rather than
    /// only the next time something tries to use it.</summary>
    Task<TikTokIntegrationResponse> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Marks the row Pending, right before TikTokOAuthController redirects the browser to TikTok's
    /// consent screen. Idempotent — safe to call again if the clinic abandons one attempt and starts another.</summary>
    Task RequestConnectAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Called by TikTokOAuthController right after it has exchanged TikTok's authorization code for
    /// tokens and fetched the account's basic profile. Stores them and marks the row Connected/healthy.</summary>
    Task<TikTokIntegrationResponse> CompleteConnectAsync(
        Guid clinicId, TikTokOAuthTokenResult tokens, TikTokAccountInfo account, CancellationToken ct = default);

    /// <summary>Called by TikTokOAuthController when TikTok denies consent or the code exchange fails. Marks
    /// the row Error with the given message so the settings page can show it.</summary>
    Task FailConnectAsync(Guid clinicId, string errorMessage, CancellationToken ct = default);

    /// <summary>Clears the connection (status, account identity, tokens) and best-effort asks TikTok to revoke
    /// the token. Always clears local state even if TikTok is unreachable.</summary>
    Task DisconnectAsync(Guid clinicId, CancellationToken ct = default);
}
