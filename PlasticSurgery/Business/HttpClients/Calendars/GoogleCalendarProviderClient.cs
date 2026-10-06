using System.Net.Http.Headers;
using System.Text.Json;
using PlasticSurgery.Business.Contracts.HttpClients.Calendars;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Calendars;

namespace PlasticSurgery.Business.HttpClients.Calendars;

/// <summary>Google Calendar's real OAuth2 + Calendar API v3, standard authorization-code flow. Needs
/// GoogleCalendar:ClientId/ClientSecret (Google Cloud Console -> APIs & Services -> Credentials -> OAuth client ID,
/// type "Web application"; enable the Google Calendar API for the project) — see appsettings.json's comment for the
/// exact redirect URI to register there.</summary>
public class GoogleCalendarProviderClient : ICalendarProviderClient
{
    // calendar.events: create/update/delete the events n8n syncs. calendar.readonly: list calendars for the picker.
    private const string Scopes = "https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/calendar.events";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public GoogleCalendarProviderClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public string Provider => CalendarProvider.Google;

    private string ClientId => _configuration["GoogleCalendar:ClientId"]
        ?? throw new InvalidOperationException("Missing GoogleCalendar:ClientId configuration.");

    private string ClientSecret => _configuration["GoogleCalendar:ClientSecret"]
        ?? throw new InvalidOperationException("Missing GoogleCalendar:ClientSecret — set it via user-secrets.");

    public string BuildAuthorizationUrl(string redirectUri, string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth"
        + $"?client_id={Uri.EscapeDataString(ClientId)}"
        + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
        + "&response_type=code"
        + $"&scope={Uri.EscapeDataString(Scopes)}"
        + "&access_type=offline"
        // prompt=consent forces Google to re-issue a refresh_token every time, not just on the very first consent —
        // without it a reconnect after a disconnect would come back with no refresh_token at all.
        + "&prompt=consent"
        + $"&state={Uri.EscapeDataString(state)}";

    public async Task<CalendarOAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        };
        var json = await PostFormAsync("https://oauth2.googleapis.com/token", form, ct);
        return ParseTokenResponse(json, fallbackRefreshToken: null);
    }

    public async Task<CalendarOAuthTokenResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        var json = await PostFormAsync("https://oauth2.googleapis.com/token", form, ct);
        // Google does not resend refresh_token on a refresh grant — keep the one we already have.
        return ParseTokenResponse(json, fallbackRefreshToken: refreshToken);
    }

    public async Task<string?> GetAccountEmailAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.TryGetProperty("email", out var email) ? email.GetString() : null;
    }

    public async Task<IReadOnlyList<CalendarCalendarOption>> ListCalendarsAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList?minAccessRole=writer");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Calendar list failed ({(int)response.StatusCode}): {body}");
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        var result = new List<CalendarCalendarOption>();
        if (json.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var name = item.TryGetProperty("summary", out var s) ? s.GetString() ?? id : id;
                var isPrimary = item.TryGetProperty("primary", out var p) && p.GetBoolean();
                result.Add(new CalendarCalendarOption(id, name, isPrimary));
            }
        }
        return result;
    }

    public async Task RevokeAsync(string? accessToken, string? refreshToken, CancellationToken ct = default)
    {
        // Google's revoke endpoint takes either token; the refresh token is the more useful one to kill since it's
        // long-lived. Best-effort only — a disconnect must succeed locally even if this call fails.
        var token = refreshToken ?? accessToken;
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var response = await _http.PostAsync(
                $"https://oauth2.googleapis.com/revoke?token={Uri.EscapeDataString(token)}", content: null, ct);
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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google token request failed ({(int)response.StatusCode}): {body}");
        }
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private static CalendarOAuthTokenResult ParseTokenResponse(JsonElement json, string? fallbackRefreshToken)
    {
        var accessToken = json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google token response had no access_token.");
        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        return new CalendarOAuthTokenResult(accessToken, refreshToken ?? fallbackRefreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }
}
