namespace PlasticSurgery.Entities.Dtos.TikTok;

/// <summary>Result of a TikTok OAuth2 code exchange or refresh.</summary>
public record TikTokOAuthTokenResult(
    string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, DateTimeOffset? RefreshTokenExpiresAt);
