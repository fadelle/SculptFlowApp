namespace PlasticSurgery.Entities.Dtos.Calendars;

/// <summary>Result of an OAuth2 code exchange or refresh — Google and Microsoft both return this same trio, just
/// under different JSON field names (mapped in each provider's own client).</summary>
public record CalendarOAuthTokenResult(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
