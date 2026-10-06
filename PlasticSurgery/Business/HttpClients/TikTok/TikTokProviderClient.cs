using System.Net.Http.Headers;
using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.TikTok;
using PlasticSurgery.Entities.Dtos.TikTok;

namespace PlasticSurgery.Business.HttpClients.TikTok;

/// <summary>TikTok Login Kit's real OAuth2 (v2) + user info endpoint. Needs TikTok:ClientKey/ClientSecret
/// (TikTok Developer Portal -> your app -> Login Kit, redirect URI registered there must exactly match
/// {App:PublicBaseUrl}/tiktok-oauth/callback) — see appsettings.json's comment. TikTok calls these
/// "client_key"/"client_secret", not "client_id" — kept as TikTok names it, not renamed to match Google's.</summary>
public class TikTokProviderClient : ITikTokProviderClient
{
    // The only scope this phase needs — basic profile (open_id/display_name/avatar). Business Messaging or
    // any other product would add its own scope here later; nothing else changes structurally.
    private const string Scope = "user.info.basic";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public TikTokProviderClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    private string ClientKey => _configuration["TikTok:ClientKey"]
        ?? throw new InvalidOperationException("Missing TikTok:ClientKey configuration.");

    private string ClientSecret => _configuration["TikTok:ClientSecret"]
        ?? throw new InvalidOperationException("Missing TikTok:ClientSecret — set it via user-secrets.");

    public string BuildAuthorizationUrl(string redirectUri, string state) =>
        "https://www.tiktok.com/v2/auth/authorize/"
        + $"?client_key={Uri.EscapeDataString(ClientKey)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + "&response_type=code"
        + $"&scope={Uri.EscapeDataString(Scope)}"
        + $"&state={Uri.EscapeDataString(state)}";

    public async Task<TikTokOAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_key"] = ClientKey,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };
        var json = await PostFormAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
        return ParseTokenResponse(json, fallbackRefreshToken: null);
    }

    public async Task<TikTokOAuthTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_key"] = ClientKey,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        var json = await PostFormAsync("https://open.tiktokapis.com/v2/oauth/token/", form, ct);
        return ParseTokenResponse(json, fallbackRefreshToken: refreshToken);
    }

    public async Task<TikTokAccountInfo> GetAccountInfoAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://open.tiktokapis.com/v2/user/info/?fields=open_id,union_id,avatar_url,display_name");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"TikTok user info request failed ({(int)response.StatusCode}): {body}");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        if (json.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code)
            && code.GetString() is { } codeStr && !string.Equals(codeStr, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : codeStr;
            throw new InvalidOperationException($"TikTok user info request failed: {message}");
        }

        var user = json.GetProperty("data").GetProperty("user");
        return new TikTokAccountInfo(
            OpenId: user.GetProperty("open_id").GetString() ?? throw new InvalidOperationException("TikTok user info had no open_id."),
            UnionId: user.TryGetProperty("union_id", out var u) ? u.GetString() : null,
            DisplayName: user.TryGetProperty("display_name", out var d) ? d.GetString() : null,
            AvatarUrl: user.TryGetProperty("avatar_url", out var a) ? a.GetString() : null);
    }

    public async Task RevokeAsync(string? accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return;
        try
        {
            var form = new Dictionary<string, string>
            {
                ["client_key"] = ClientKey,
                ["client_secret"] = ClientSecret,
                ["token"] = accessToken
            };
            using var response = await _http.PostAsync("https://open.tiktokapis.com/v2/oauth/revoke/", new FormUrlEncodedContent(form), ct);
        }
        catch
        {
            // Swallowed deliberately — see interface doc comment.
        }
    }

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        // TikTok's token endpoint returns 200 even for some errors, with the error in the body instead.
        if (!response.IsSuccessStatusCode || json.TryGetProperty("error", out _))
        {
            throw new InvalidOperationException($"TikTok token request failed ({(int)response.StatusCode}): {body}");
        }
        return json;
    }

    private static TikTokOAuthTokenResult ParseTokenResponse(JsonElement json, string? fallbackRefreshToken)
    {
        var accessToken = json.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("TikTok token response had no access_token.");
        }
        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 86400;
        var refreshExpiresIn = json.TryGetProperty("refresh_expires_in", out var rexp) ? rexp.GetInt32() : (int?)null;
        return new TikTokOAuthTokenResult(
            accessToken,
            refreshToken ?? fallbackRefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            refreshExpiresIn.HasValue ? DateTimeOffset.UtcNow.AddSeconds(refreshExpiresIn.Value) : null);
    }
}
