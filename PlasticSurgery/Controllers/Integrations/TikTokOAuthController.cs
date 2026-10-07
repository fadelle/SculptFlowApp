using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.HttpClients.TikTok;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.TikTok;

namespace PlasticSurgery.Controllers.Integrations;

/// <summary>The real, browser-redirect OAuth2 flow for TikTok Login Kit — SculptFlow itself talks to TikTok
/// here (see ITikTokProviderClient); n8n has no role in any of it. Same shape as CalendarOAuthController:
/// not a JSON API — actions redirect the whole page, first to TikTok's consent screen and then back to
/// Settings → Channels &amp; Integrations. Extends Controller (not DashboardApiController/ControllerBase):
/// TempData — used to carry the result back across the redirect — is only wired up on the full Controller
/// base.</summary>
[Route("tiktok-oauth")]
[Authorize]
public class TikTokOAuthController : Controller
{
    private const string StatePurpose = "PlasticSurgery.TikTokOAuthState";
    private TimeSpan StateLifetime => TimeSpan.FromMinutes(_config.IntegrationsOAuthStateLifetimeMinutes);
    private const string SettingsPagePath = "/settings/integrations";

    private readonly ICurrentClinicContext _clinicContext;
    private readonly ITikTokIntegrationService _tiktok;
    private readonly ITikTokProviderClient _provider;
    private readonly IConfiguration _configuration;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ILogger<TikTokOAuthController> _logger;
    private readonly IConfigManager _config;

    public TikTokOAuthController(
        ICurrentClinicContext clinicContext, ITikTokIntegrationService tiktok, ITikTokProviderClient provider,
        IConfiguration configuration, IDataProtectionProvider dataProtection, ILogger<TikTokOAuthController> logger, IConfigManager config)
    {
        _config = config;
        _clinicContext = clinicContext;
        _tiktok = tiktok;
        _provider = provider;
        _configuration = configuration;
        _stateProtector = dataProtection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();
        _logger = logger;
    }

    private Task<Guid?> GetClinicIdAsync(CancellationToken ct) => _clinicContext.GetClinicIdAsync(ct);

    /// <summary>Marks the row Pending and sends the browser to TikTok's consent screen.</summary>
    [HttpGet("connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        var clinicId = await GetClinicIdAsync(ct);
        if (clinicId is null) return Forbid();

        try
        {
            var redirectUri = BuildRedirectUri();
            var state = _stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(clinicId.Value)), StateLifetime);
            var authorizationUrl = _provider.BuildAuthorizationUrl(redirectUri, state);

            await _tiktok.RequestConnectAsync(clinicId.Value, ct);
            return Redirect(authorizationUrl);
        }
        catch (InvalidOperationException ex)
        {
            // Missing ClientKey/ClientSecret (not configured yet) or a bad App:PublicBaseUrl — an
            // operator/config problem, not something retrying will fix, so don't mark the row Pending for it.
            _logger.LogError(ex, "TikTok OAuth connect could not start.");
            return RedirectWithError($"TikTok isn't set up yet on this server: {ex.Message}");
        }
    }

    /// <summary>Where TikTok sends the browser back after consent (or denial/error).</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken ct)
    {
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
            _logger.LogWarning(ex, "TikTok OAuth state failed to validate (expired or tampered).");
        }

        if (oauthState is null || oauthState.ClinicId != clinicId.Value)
        {
            await _tiktok.FailConnectAsync(clinicId.Value, "The connection attempt expired or was invalid — please try again.", ct);
            return RedirectWithError("The connection attempt expired or was invalid — please try again.");
        }

        if (!string.IsNullOrEmpty(error))
        {
            var message = errorDescription ?? error;
            await _tiktok.FailConnectAsync(clinicId.Value, message, ct);
            return RedirectWithError(message);
        }

        if (string.IsNullOrEmpty(code))
        {
            await _tiktok.FailConnectAsync(clinicId.Value, "TikTok did not return an authorization code.", ct);
            return RedirectWithError("TikTok did not return an authorization code.");
        }

        try
        {
            var redirectUri = BuildRedirectUri();
            var tokens = await _provider.ExchangeCodeAsync(code, redirectUri, ct);
            var account = await _provider.GetAccountInfoAsync(tokens.AccessToken, ct);
            await _tiktok.CompleteConnectAsync(clinicId.Value, tokens, account, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TikTok OAuth code exchange failed.");
            await _tiktok.FailConnectAsync(clinicId.Value, $"Couldn't finish connecting: {ex.Message}", ct);
            return RedirectWithError($"Couldn't finish connecting: {ex.Message}");
        }

        TempData["StatusMessage"] = "TikTok connected.";
        return Redirect(SettingsPagePath);
    }

    private IActionResult RedirectWithError(string message)
    {
        TempData["ErrorMessage"] = message;
        return Redirect(SettingsPagePath);
    }

    /// <summary>Same App:PublicBaseUrl-first convention as TelegramIntegrationService.ResolvePublicBaseUrl /
    /// CalendarOAuthController.BuildRedirectUri — required here in particular, since this exact URL must match
    /// what's registered for the Login Kit app in the TikTok Developer Portal.</summary>
    private string BuildRedirectUri()
    {
        var configured = _configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("App:PublicBaseUrl must be a full https:// URL for TikTok OAuth callbacks.");
            }
            return $"{configured}/tiktok-oauth/callback";
        }

        var host = Request.Host;
        var isLocal = host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                      || host.Host.StartsWith("127.") || host.Host == "::1" || host.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        if (!isLocal)
        {
            return $"https://{host}/tiktok-oauth/callback";
        }

        throw new InvalidOperationException(
            "TikTok OAuth needs a public HTTPS address for its redirect URI. Set App:PublicBaseUrl (e.g. https://your-app.onrender.com, or an https tunnel URL when testing locally).");
    }

    private record OAuthState(Guid ClinicId);
}
