using System.Text.Json;

namespace PlasticSurgery.Services;

/// <summary>
/// NOTE — first real integration against Meta's Graph API in this project: these calls follow
/// Meta's documented JS-SDK/config_id login pattern (no redirect_uri on the code exchange, since
/// the login happens in a popup via FB.login() rather than a full-page OAuth redirect), but this
/// hasn't been exercised against a live Meta app yet. If Meta returns a different error shape than
/// expected, the raw response body is included in the thrown exception's message — send that back
/// and it'll be quick to fix.
/// </summary>
public class MetaGraphClient : IMetaGraphClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public MetaGraphClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    private string AppId => _configuration["Meta:AppId"]
        ?? throw new InvalidOperationException("Missing Meta:AppId configuration.");

    private string AppSecret => _configuration["Meta:AppSecret"]
        ?? throw new InvalidOperationException("Missing Meta:AppSecret — set it via user-secrets.");

    private string Version => _configuration["Meta:GraphApiVersion"] ?? "v21.0";

    private string GraphUrl(string path) => $"https://graph.facebook.com/{Version}/{path}";

    public async Task<string> ExchangeCodeForTokenAsync(string code, string? redirectUri = null, CancellationToken ct = default)
    {
        var url = GraphUrl("oauth/access_token")
            + $"?client_id={Uri.EscapeDataString(AppId)}"
            + $"&client_secret={Uri.EscapeDataString(AppSecret)}"
            + $"&code={Uri.EscapeDataString(code)}";

        if (!string.IsNullOrEmpty(redirectUri))
        {
            url += $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";
        }

        var json = await GetJsonAsync(url, ct);
        return json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Meta token exchange response had no access_token.");
    }

    public async Task<string> GetLongLivedTokenAsync(string shortLivedToken, CancellationToken ct = default)
    {
        var url = GraphUrl("oauth/access_token")
            + "?grant_type=fb_exchange_token"
            + $"&client_id={Uri.EscapeDataString(AppId)}"
            + $"&client_secret={Uri.EscapeDataString(AppSecret)}"
            + $"&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}";

        var json = await GetJsonAsync(url, ct);
        return json.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Meta long-lived token exchange response had no access_token.");
    }

    public async Task<FacebookPageInfo?> GetFirstManagedPageAsync(string userAccessToken, CancellationToken ct = default)
    {
        var url = GraphUrl("me/accounts") + $"?access_token={Uri.EscapeDataString(userAccessToken)}";
        var json = await GetJsonAsync(url, ct);

        if (!json.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        // MVP: take the first Page the user manages. If a clinic's Facebook user admins multiple
        // Pages, this picks arbitrarily — a Page-picker UI is the natural next step here.
        var first = data[0];
        return new FacebookPageInfo(
            PageId: first.GetProperty("id").GetString()!,
            PageName: first.GetProperty("name").GetString()!,
            PageAccessToken: first.GetProperty("access_token").GetString()!);
    }

    public async Task<WhatsAppPhoneNumberInfo?> GetWhatsAppPhoneNumberAsync(string phoneNumberId, string accessToken, CancellationToken ct = default)
    {
        var url = GraphUrl(phoneNumberId)
            + $"?fields=display_phone_number,verified_name"
            + $"&access_token={Uri.EscapeDataString(accessToken)}";

        var json = await GetJsonAsync(url, ct);
        var display = json.TryGetProperty("display_phone_number", out var d) ? d.GetString() : null;
        if (display is null)
        {
            return null;
        }

        var verifiedName = json.TryGetProperty("verified_name", out var v) ? v.GetString() : null;
        return new WhatsAppPhoneNumberInfo(display, verifiedName);
    }

    public async Task<IReadOnlyList<string>> GetGrantedScopesAsync(string accessToken, CancellationToken ct = default)
    {
        // /debug_token needs an app access token (app_id|app_secret) to authenticate the *call*,
        // separate from `accessToken`, which is the token being inspected.
        var appToken = $"{AppId}|{AppSecret}";
        var url = GraphUrl("debug_token")
            + $"?input_token={Uri.EscapeDataString(accessToken)}"
            + $"&access_token={Uri.EscapeDataString(appToken)}";

        var json = await GetJsonAsync(url, ct);
        var data = json.GetProperty("data");

        var scopes = new List<string>();
        if (data.TryGetProperty("scopes", out var scopesEl))
        {
            foreach (var s in scopesEl.EnumerateArray())
            {
                if (s.GetString() is { } scope) scopes.Add(scope);
            }
        }
        return scopes;
    }

    public async Task<IReadOnlyList<ClientWabaInfo>> GetClientWhatsAppBusinessAccountsAsync(string accessToken, CancellationToken ct = default)
    {
        var result = new List<ClientWabaInfo>();

        var businessesUrl = GraphUrl("me/businesses") + $"?access_token={Uri.EscapeDataString(accessToken)}";
        var businessesJson = await GetJsonAsync(businessesUrl, ct);
        if (!businessesJson.TryGetProperty("data", out var businesses))
        {
            return result;
        }

        foreach (var business in businesses.EnumerateArray())
        {
            var businessId = business.GetProperty("id").GetString()!;
            var businessName = business.TryGetProperty("name", out var bn) ? bn.GetString() ?? businessId : businessId;

            // Check both edges: "owned" (WABAs this business created itself) and "client" (WABAs a
            // different business shared with this one as a Tech Provider/partner) — which one
            // applies depends on whether the login was for your own business or someone else's.
            foreach (var (edge, relationship) in new[] { ("owned_whatsapp_business_accounts", "owned"), ("client_whatsapp_business_accounts", "client") })
            {
                var wabasUrl = GraphUrl($"{businessId}/{edge}")
                    + $"?access_token={Uri.EscapeDataString(accessToken)}";

                JsonElement wabasJson;
                try
                {
                    wabasJson = await GetJsonAsync(wabasUrl, ct);
                }
                catch (MetaGraphApiException)
                {
                    continue; // this business may not expose that edge to us — skip, don't fail the whole discovery
                }

                if (!wabasJson.TryGetProperty("data", out var wabas))
                {
                    continue;
                }

                foreach (var waba in wabas.EnumerateArray())
                {
                    var wabaId = waba.GetProperty("id").GetString()!;
                    var wabaName = waba.TryGetProperty("name", out var wn) ? wn.GetString() ?? wabaId : wabaId;
                    result.Add(new ClientWabaInfo(businessId, businessName, wabaId, wabaName, relationship));
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<WabaPhoneNumberInfo>> GetPhoneNumbersForWabaAsync(string wabaId, string accessToken, CancellationToken ct = default)
    {
        var url = GraphUrl($"{wabaId}/phone_numbers")
            + $"?fields=display_phone_number,verified_name"
            + $"&access_token={Uri.EscapeDataString(accessToken)}";

        var json = await GetJsonAsync(url, ct);
        var result = new List<WabaPhoneNumberInfo>();
        if (!json.TryGetProperty("data", out var data))
        {
            return result;
        }

        foreach (var phone in data.EnumerateArray())
        {
            var id = phone.GetProperty("id").GetString()!;
            var display = phone.TryGetProperty("display_phone_number", out var d) ? d.GetString() ?? id : id;
            var verifiedName = phone.TryGetProperty("verified_name", out var v) ? v.GetString() : null;
            result.Add(new WabaPhoneNumberInfo(id, display, verifiedName));
        }
        return result;
    }

    public async Task RegisterPhoneNumberAsync(string phoneNumberId, string accessToken, string pin, CancellationToken ct = default)
    {
        var url = GraphUrl($"{phoneNumberId}/register") + $"?access_token={Uri.EscapeDataString(accessToken)}";
        var payload = new { messaging_product = "whatsapp", pin };
        await PostJsonAsync(url, payload, ct);
    }

    public async Task<MetaTemplateCreateResult> CreateMessageTemplateAsync(
        string wabaId, string accessToken, string name, string category, string language,
        object components, CancellationToken ct = default)
    {
        var url = GraphUrl($"{wabaId}/message_templates") + $"?access_token={Uri.EscapeDataString(accessToken)}";
        var payload = new { name, category = category.ToUpperInvariant(), language, components };

        var json = await PostJsonAsync(url, payload, ct);
        var id = json.GetProperty("id").GetString()
            ?? throw new MetaGraphApiException("Meta template creation response had no id.");
        // Meta omits "status" from the create response on some API versions — PENDING is the
        // documented default for a freshly submitted template either way.
        var status = json.TryGetProperty("status", out var s) ? s.GetString() ?? "PENDING" : "PENDING";
        return new MetaTemplateCreateResult(id, status);
    }

    public async Task<MetaTemplateStatusResult> GetMessageTemplateStatusAsync(string metaTemplateId, string accessToken, CancellationToken ct = default)
    {
        var url = GraphUrl(metaTemplateId) + $"?fields=status,rejected_reason&access_token={Uri.EscapeDataString(accessToken)}";
        var json = await GetJsonAsync(url, ct);

        var status = json.TryGetProperty("status", out var s) ? s.GetString() ?? "PENDING" : "PENDING";
        var rejectedReason = json.TryGetProperty("rejected_reason", out var r) ? r.GetString() : null;
        return new MetaTemplateStatusResult(status, rejectedReason is "NONE" ? null : rejectedReason);
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new MetaGraphApiException(
                $"Meta Graph API call failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<JsonElement> PostJsonAsync(string url, object payload, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new MetaGraphApiException(
                $"Meta Graph API call failed ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }

        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

/// <summary>Thrown when a Meta Graph API call returns a non-success response — the message carries Meta's raw error body.</summary>
public class MetaGraphApiException : Exception
{
    public MetaGraphApiException(string message) : base(message) { }
}
