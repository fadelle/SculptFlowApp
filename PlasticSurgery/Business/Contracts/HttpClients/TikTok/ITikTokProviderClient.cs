using PlasticSurgery.Entities.Dtos.TikTok;

namespace PlasticSurgery.Business.Contracts.HttpClients.TikTok;

/// <summary>SculptFlow's own direct TikTok Login Kit OAuth2 client — no n8n involvement, same shape as
/// ICalendarProviderClient (Google/Outlook), TikTok's own real API surface underneath. Only RevokeAsync is
/// allowed to swallow its own failures (best-effort cleanup on disconnect); every other method throws on
/// failure so TikTokIntegrationService can mark the connection Error/unhealthy with a real message.</summary>
public interface ITikTokProviderClient
{
    /// <summary>The URL to send the browser to for consent. state must be an opaque, server-generated value
    /// (TikTokOAuthController protects it) — never anything derived only from client input.</summary>
    string BuildAuthorizationUrl(string redirectUri, string state);

    Task<TikTokOAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    Task<TikTokOAuthTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);

    Task<TikTokAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Tells TikTok the clinic is disconnecting, so it can invalidate the token server-side. Never
    /// throws — a disconnect from the clinic's own settings page must succeed locally even if TikTok can't be
    /// reached; tokens are cleared locally regardless.</summary>
    Task RevokeAsync(string? accessToken, CancellationToken ct = default);
}
