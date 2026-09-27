using System.Net.Http.Headers;
using System.Text.Json;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>Outlook/Microsoft 365 calendars via the Microsoft identity platform v2.0 (OAuth2) + Microsoft Graph.
/// Needs MicrosoftCalendar:ClientId/ClientSecret/TenantId (Azure Portal -> Microsoft Entra ID -> App registrations,
/// platform "Web") — see appsettings.json's comment for the exact redirect URI to register there. TenantId defaults
/// to "common" so both work/school and personal Microsoft accounts can connect.</summary>
public class OutlookCalendarProviderClient : ICalendarProviderClient
{
    private const string Scopes = "offline_access https://graph.microsoft.com/Calendars.ReadWrite https://graph.microsoft.com/User.Read";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public OutlookCalendarProviderClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public string Provider => CalendarProvider.Outlook;

    private string ClientId => _configuration["MicrosoftCalendar:ClientId"]
        ?? throw new InvalidOperationException("Missing MicrosoftCalendar:ClientId configuration.");

    private string ClientSecret => _configuration["MicrosoftCalendar:ClientSecret"]
        ?? throw new InvalidOperationException("Missing MicrosoftCalendar:ClientSecret — set it via user-secrets.");

    private string TenantId => string.IsNullOrWhiteSpace(_configuration["MicrosoftCalendar:TenantId"])
        ? "common" : _configuration["MicrosoftCalendar:TenantId"]!;

    private string AuthorizeUrl => $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/authorize";
    private string TokenUrl => $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";

    public string BuildAuthorizationUrl(string redirectUri, string state) =>
        AuthorizeUrl
        + $"?client_id={Uri.EscapeDataString(ClientId)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + "&response_type=code"
        + "&response_mode=query"
        + $"&scope={Uri.EscapeDataString(Scopes)}"
        + $"&state={Uri.EscapeDataString(state)}";

    public async Task<CalendarOAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scopes
        };
        var json = await PostFormAsync(TokenUrl, form, ct);
        return ParseTokenResponse(json, fallbackRefreshToken: null);
    }

    public async Task<CalendarOAuthTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = Scopes
        };
        var json = await PostFormAsync(TokenUrl, form, ct);
        return ParseTokenResponse(json, fallbackRefreshToken: refreshToken);
    }

    public async Task<string?> GetAccountEmailAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (json.TryGetProperty("mail", out var mail) && mail.GetString() is { Length: > 0 } m) return m;
        return json.TryGetProperty("userPrincipalName", out var upn) ? upn.GetString() : null;
    }

    public async Task<IReadOnlyList<CalendarCalendarOption>> ListCalendarsAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/calendars?$select=id,name,isDefaultCalendar");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Outlook Calendar list failed ({(int)response.StatusCode}): {body}");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var result = new List<CalendarCalendarOption>();
        if (json.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
                var isPrimary = item.TryGetProperty("isDefaultCalendar", out var p) && p.ValueKind == JsonValueKind.True;
                result.Add(new CalendarCalendarOption(id, name, isPrimary));
            }
        }
        return result;
    }

    public Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct = default)
    {
        // Microsoft Graph has no per-app token revocation endpoint for confidential-client refresh tokens (the
        // closest, /me/revokeSignInSessions, kills ALL of the user's sessions everywhere — too broad for a
        // per-integration disconnect). Nothing to call; the tokens are simply dropped locally and the disconnect
        // still fully stops SculptFlow from using them.
        return Task.CompletedTask;
    }

    private async Task<JsonElement> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Microsoft token request failed ({(int)response.StatusCode}): {body}");
        }
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static CalendarOAuthTokenResult ParseTokenResponse(JsonElement json, string? fallbackRefreshToken)
    {
        var accessToken = json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Microsoft token response had no access_token.");
        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        return new CalendarOAuthTokenResult(accessToken, refreshToken ?? fallbackRefreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }
}
