using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.HttpClients.Calendars;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Calendars;
using Omni.App.Common.Enums;
using Microsoft.Extensions.Localization;
using Omni.App.Common.Helpers;

namespace Omni.App.Controllers.Integrations;

/// <summary>The real, browser-redirect OAuth2 flow for Calendar Integrations — Omni itself talks to Google/
/// Microsoft here (see ICalendarProviderClient); n8n has no role in connect/disconnect at all (only in
/// TriggerBookingSyncAsync's sync call, elsewhere). Not a JSON API — actions redirect the whole page, first to
/// the provider's consent screen and then back to Settings → Calendar Integrations, the same way
/// CalendarIntegrations.cshtml's plain server-form actions do. Extends Controller (not DashboardApiController):
/// TempData — used to carry the result back to the settings page across the redirect — is only wired up on the
/// full Controller base, not ControllerBase.</summary>
[Route("calendar-oauth")]
[Authorize]
public class CalendarOAuthController : Controller
{
    private const string StatePurpose = "Omni.App.CalendarOAuthState";
    private TimeSpan StateLifetime => TimeSpan.FromMinutes(_config.IntegrationsOAuthStateLifetimeMinutes);
    private const string SettingsPagePath = "/settings/calendar-integrations";

    private readonly ICurrentClientContext _clientContext;
    private readonly ICalendarIntegrationService _calendar;
    private readonly IEnumerable<ICalendarProviderClient> _providerClients;
    private readonly IConfiguration _configuration;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ILogger<CalendarOAuthController> _logger;
    private readonly IConfigManager _config;
    private readonly IStringLocalizer _text;

    public CalendarOAuthController(
        ICalendarIntegrationService calendar, ICurrentClientContext clientContext,
        IEnumerable<ICalendarProviderClient> providerClients, IConfiguration configuration,
        IDataProtectionProvider dataProtection, ILogger<CalendarOAuthController> logger, IConfigManager config, IStringLocalizer text)
    {
        _config = config;
        _text = text;
        _clientContext = clientContext;
        _calendar = calendar;
        _providerClients = providerClients;
        _configuration = configuration;
        _stateProtector = dataProtection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();
        _logger = logger;
    }

    private Task<Guid?> GetClientIdAsync(CancellationToken ct) => _clientContext.GetClientIdAsync(ct);

    /// <summary>Marks the row Pending and sends the browser to the provider's consent screen.</summary>
    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(string provider, CancellationToken ct)
    {
        if (!CalendarProvider.All.Contains(provider)) return RedirectWithError(_text["calendars.errors.unknownProvider", provider].Value);

        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var client = ResolveProviderClient(provider);
            var redirectUri = BuildRedirectUri(provider);
            var state = _stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(clientId.Value, provider)), StateLifetime);
            var authorizationUrl = client.BuildAuthorizationUrl(redirectUri, state);

            await _calendar.RequestConnectAsync(clientId.Value, provider, ct);
            return Redirect(authorizationUrl);
        }
        catch (InvalidOperationException ex)
        {
            // Missing ClientId/ClientSecret (not configured yet) or a bad App:PublicBaseUrl — an operator/config
            // problem, not something retrying will fix, so don't mark the row Pending for it.
            _logger.LogError(ex, "Calendar OAuth connect for {Provider} could not start.", provider);
            return RedirectWithError(_text["oauth.errors.notSetUp", ProviderLabel(provider), _text.Localize(ex)].Value);
        }
    }

    /// <summary>Where the provider sends the browser back after consent (or denial/error).</summary>
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider, [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken ct)
    {
        if (!CalendarProvider.All.Contains(provider)) return RedirectWithError(_text["calendars.errors.unknownProvider", provider].Value);

        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        OAuthState? oauthState = null;
        try
        {
            if (!string.IsNullOrEmpty(state))
            {
                oauthState = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Calendar OAuth state for {Provider} failed to validate (expired or tampered).", provider);
        }

        if (oauthState is null || oauthState.ClientId != clientId.Value || oauthState.Provider != provider)
        {
            var expired = _text["oauth.errors.expired"].Value;
            await _calendar.FailConnectAsync(clientId.Value, provider, expired, ct);
            return RedirectWithError(expired);
        }

        if (!string.IsNullOrEmpty(error))
        {
            var message = errorDescription ?? error;
            await _calendar.FailConnectAsync(clientId.Value, provider, message, ct);
            return RedirectWithError(message);
        }

        if (string.IsNullOrEmpty(code))
        {
            var noCode = _text["oauth.errors.noCode", ProviderLabel(provider)].Value;
            await _calendar.FailConnectAsync(clientId.Value, provider, noCode, ct);
            return RedirectWithError(noCode);
        }

        try
        {
            var client = ResolveProviderClient(provider);
            var redirectUri = BuildRedirectUri(provider);
            var tokens = await client.ExchangeCodeAsync(code, redirectUri, ct);
            var email = await client.GetAccountEmailAsync(tokens.AccessToken, ct);
            await _calendar.CompleteConnectAsync(clientId.Value, provider, tokens, email, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Calendar OAuth code exchange failed for {Provider}.", provider);
            var failed = _text["oauth.errors.connectFailed", _text.Localize(ex)].Value;
            await _calendar.FailConnectAsync(clientId.Value, provider, failed, ct);
            return RedirectWithError(failed);
        }

        TempData["StatusMessage"] = _text["oauth.connected", ProviderLabel(provider)].Value;
        return Redirect(SettingsPagePath);
    }

    private IActionResult RedirectWithError(string message)
    {
        TempData["ErrorMessage"] = message;
        return Redirect(SettingsPagePath);
    }

    private ICalendarProviderClient ResolveProviderClient(string provider) =>
        _providerClients.FirstOrDefault(c => c.Provider == provider)
        ?? throw new InvalidOperationException($"No calendar provider client registered for '{provider}'.");

    private static string ProviderLabel(string provider) => provider == CalendarProvider.Google ? "Google Calendar" : "Outlook Calendar";

    /// <summary>Same App:PublicBaseUrl-first convention as TelegramIntegrationService.ResolvePublicBaseUrl — required
    /// here in particular, since this exact URL must match what's registered in the Google/Azure app console.</summary>
    private string BuildRedirectUri(string provider)
    {
        var configured = _configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("App:PublicBaseUrl must be a full https:// URL for calendar OAuth callbacks.");
            }
            return $"{configured}/calendar-oauth/{provider}/callback";
        }

        var host = Request.Host;
        var isLocal = host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                      || host.Host.StartsWith("127.") || host.Host == "::1" || host.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        if (!isLocal)
        {
            return $"https://{host}/calendar-oauth/{provider}/callback";
        }

        throw new InvalidOperationException(
            "Calendar OAuth needs a public HTTPS address for its redirect URI. Set App:PublicBaseUrl (e.g. https://your-app.onrender.com, or an https tunnel URL when testing locally).");
    }

    private record OAuthState(Guid ClientId, string Provider);
}
