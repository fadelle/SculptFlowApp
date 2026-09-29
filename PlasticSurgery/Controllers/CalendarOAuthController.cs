using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>The real, browser-redirect OAuth2 flow for Calendar Integrations — SculptFlow itself talks to Google/
/// Microsoft here (see ICalendarProviderClient); n8n has no role in connect/disconnect at all (only in
/// TriggerAppointmentSyncAsync's sync call, elsewhere). Not a JSON API — actions redirect the whole page, first to
/// the provider's consent screen and then back to Settings → Calendar Integrations, the same way
/// CalendarIntegrations.cshtml's plain server-form actions do. Extends Controller (not DashboardApiController):
/// TempData — used to carry the result back to the settings page across the redirect — is only wired up on the
/// full Controller base, not ControllerBase.</summary>
[Route("calendar-oauth")]
[Authorize]
public class CalendarOAuthController : Controller
{
    private const string StatePurpose = "PlasticSurgery.CalendarOAuthState";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);
    private const string SettingsPagePath = "/settings/calendar-integrations";

    private readonly ICurrentClinicContext _clinicContext;
    private readonly ICalendarIntegrationService _calendar;
    private readonly IEnumerable<ICalendarProviderClient> _providerClients;
    private readonly IConfiguration _configuration;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ILogger<CalendarOAuthController> _logger;

    public CalendarOAuthController(
        ICalendarIntegrationService calendar, ICurrentClinicContext clinicContext,
        IEnumerable<ICalendarProviderClient> providerClients, IConfiguration configuration,
        IDataProtectionProvider dataProtection, ILogger<CalendarOAuthController> logger)
    {
        _clinicContext = clinicContext;
        _calendar = calendar;
        _providerClients = providerClients;
        _configuration = configuration;
        _stateProtector = dataProtection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();
        _logger = logger;
    }

    private Task<Guid?> GetClinicIdAsync(CancellationToken ct) => _clinicContext.GetClinicIdAsync(ct);

    /// <summary>Marks the row Pending and sends the browser to the provider's consent screen.</summary>
    [HttpGet("{provider}/connect")]
    public async Task<IActionResult> Connect(string provider, CancellationToken ct)
    {
        if (!CalendarProvider.All.Contains(provider)) return RedirectWithError("Unknown calendar provider.");

        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var client = ResolveProviderClient(provider);
            var redirectUri = BuildRedirectUri(provider);
            var state = _stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(clinicId.Value, provider)), StateLifetime);
            var authorizationUrl = client.BuildAuthorizationUrl(redirectUri, state);

            await _calendar.RequestConnectAsync(clinicId.Value, provider, ct);
            return Redirect(authorizationUrl);
        }
        catch (InvalidOperationException ex)
        {
            // Missing ClientId/ClientSecret (not configured yet) or a bad App:PublicBaseUrl — an operator/config
            // problem, not something retrying will fix, so don't mark the row Pending for it.
            _logger.LogError(ex, "Calendar OAuth connect for {Provider} could not start.", provider);
            return RedirectWithError($"{ProviderLabel(provider)} isn't set up yet on this server: {ex.Message}");
        }
    }

    /// <summary>Where the provider sends the browser back after consent (or denial/error).</summary>
    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        string provider, [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken ct)
    {
        if (!CalendarProvider.All.Contains(provider)) return RedirectWithError("Unknown calendar provider.");

        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

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

        if (oauthState is null || oauthState.ClinicId != clinicId.Value || oauthState.Provider != provider)
        {
            await _calendar.FailConnectAsync(clinicId.Value, provider, "The connection attempt expired or was invalid — please try again.", ct);
            return RedirectWithError("The connection attempt expired or was invalid — please try again.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            var message = errorDescription ?? error;
            await _calendar.FailConnectAsync(clinicId.Value, provider, message, ct);
            return RedirectWithError(message);
        }

        if (string.IsNullOrEmpty(code))
        {
            await _calendar.FailConnectAsync(clinicId.Value, provider, "The provider did not return an authorization code.", ct);
            return RedirectWithError("The provider did not return an authorization code.");
        }

        try
        {
            var client = ResolveProviderClient(provider);
            var redirectUri = BuildRedirectUri(provider);
            var tokens = await client.ExchangeCodeAsync(code, redirectUri, ct);
            var email = await client.GetAccountEmailAsync(tokens.AccessToken, ct);
            await _calendar.CompleteConnectAsync(clinicId.Value, provider, tokens, email, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Calendar OAuth code exchange failed for {Provider}.", provider);
            await _calendar.FailConnectAsync(clinicId.Value, provider, $"Couldn't finish connecting: {ex.Message}", ct);
            return RedirectWithError($"Couldn't finish connecting: {ex.Message}");
        }

        TempData["StatusMessage"] = $"{ProviderLabel(provider)} connected.";
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

    private record OAuthState(Guid ClinicId, string Provider);
}
