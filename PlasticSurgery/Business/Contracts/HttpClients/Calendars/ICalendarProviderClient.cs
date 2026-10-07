using PlasticSurgery.Entities.Dtos.Calendars;

namespace PlasticSurgery.Business.Contracts.HttpClients.Calendars;

/// <summary>One real OAuth2 client for a calendar provider (Google or Outlook) — SculptFlow's own direct
/// integration, resolved by Provider the same way MessageService resolves an IChannelSender by Channel. Every
/// method talks to the provider's actual API; there is no n8n involvement anywhere in this interface. Only
/// RevokeAsync is allowed to swallow its own failures (best-effort cleanup on disconnect) — every other method
/// throws on failure so CalendarIntegrationService can mark the integration Error/unhealthy with a real message.</summary>
public interface ICalendarProviderClient
{
    string Provider { get; }

    /// <summary>The URL to send the browser to for consent. state must be an opaque, server-generated value
    /// (CalendarOAuthController protects it) — never anything derived only from client input.</summary>
    string BuildAuthorizationUrl(string redirectUri, string state);

    Task<CalendarOAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);

    Task<CalendarOAuthTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>Best-effort account label for display only (e.g. "jane@clinic.com") — null if the provider's
    /// profile endpoint doesn't return one.</summary>
    Task<string?> GetAccountEmailAsync(string accessToken, CancellationToken ct = default);

    Task<IReadOnlyList<CalendarCalendarOption>> ListCalendarsAsync(string accessToken, CancellationToken ct = default);

    /// <summary>Tells the provider the clinic is disconnecting, so it can invalidate the token(s) server-side.
    /// Never throws — a provider that can't be reached for revocation is not a reason to fail a disconnect the
    /// clinic is doing from their own SculptFlow settings page; the tokens are cleared locally regardless.</summary>
    Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct = default);
}
