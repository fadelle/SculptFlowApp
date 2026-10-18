using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.HttpClients.TikTok;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.TikTok;
using Microsoft.Extensions.Localization;
using Omni.App.Common.Helpers;

namespace Omni.App.Controllers.Integrations;

/// <summary>The real, browser-redirect OAuth2 flow for TikTok Login Kit — Omni itself talks to TikTok
/// here (see ITikTokProviderClient); n8n has no role in any of it. Same shape as CalendarOAuthController:
/// not a JSON API — actions redirect the whole page, first to TikTok's consent screen and then back to
/// Settings → Channels &amp; Integrations. Extends Controller (not DashboardApiController/ControllerBase):
/// TempData — used to carry the result back across the redirect — is only wired up on the full Controller
/// base.</summary>
[Route("tiktok-oauth")]
[Authorize]
public class TikTokOAuthController : Controller
{
    private const string StatePurpose = "Omni.App.TikTokOAuthState";
    private TimeSpan StateLifetime => TimeSpan.FromMinutes(_config.IntegrationsOAuthStateLifetimeMinutes);
    private const string SettingsPagePath = "/settings/integrations";

    private readonly ICurrentClientContext _clientContext;
    private readonly ITikTokIntegrationService _tiktok;
    private readonly ITikTokProviderClient _provider;
    private readonly IConfiguration _configuration;
    private readonly ITimeLimitedDataProtector _stateProtector;
    private readonly ILogger<TikTokOAuthController> _logger;
    private readonly IConfigManager _config;
    private readonly IStringLocalizer _text;

    public TikTokOAuthController(
        ICurrentClientContext clientContext, ITikTokIntegrationService tiktok, ITikTokProviderClient provider,
        IConfiguration configuration, IDataProtectionProvider dataProtection, ILogger<TikTokOAuthController> logger, IConfigManager config,
        IStringLocalizer text)
    {
        _config = config;
        _text = text;
        _clientContext = clientContext;
        _tiktok = tiktok;
        _provider = provider;
        _configuration = configuration;
        _stateProtector = dataProtection.CreateProtector(StatePurpose).ToTimeLimitedDataProtector();
        _logger = logger;
    }

    private Task<Guid?> GetClientIdAsync(CancellationToken ct) => _clientContext.GetClientIdAsync(ct);

    /// <summary>Marks the row Pending and sends the browser to TikTok's consent screen.</summary>
    [HttpGet("connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var redirectUri = BuildRedirectUri();
            var state = _stateProtector.Protect(JsonSerializer.Serialize(new OAuthState(clientId.Value)), StateLifetime);
            var authorizationUrl = _provider.BuildAuthorizationUrl(redirectUri, state);

            await _tiktok.RequestConnectAsync(clientId.Value, ct);
            return Redirect(authorizationUrl);
        }
        catch (InvalidOperationException ex)
        {
            // Missing ClientKey/ClientSecret (not configured yet) or a bad App:PublicBaseUrl — an
            // operator/config problem, not something retrying will fix, so don't mark the row Pending for it.
            _logger.LogError(ex, "TikTok OAuth connect could not start.");
            return RedirectWithError(_text["oauth.errors.notSetUp", TikTokName, _text.Localize(ex)].Value);
        }
    }

    /// <summary>Where TikTok sends the browser back after consent (or denial/error).</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery(Name = "error_description")] string? errorDescription, CancellationToken ct)
    {
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
            _logger.LogWarning(ex, "TikTok OAuth state failed to validate (expired or tampered).");
        }

        if (oauthState is null || oauthState.ClientId != clientId.Value)
        {
            var expired = _text["oauth.errors.expired"].Value;
            await _tiktok.FailConnectAsync(clientId.Value, expired, ct);
            return RedirectWithError(expired);
        }

        if (!string.IsNullOrEmpty(error))
        {
            var message = errorDescription ?? error;
            await _tiktok.FailConnectAsync(clientId.Value, message, ct);
            return RedirectWithError(message);
        }

        if (string.IsNullOrEmpty(code))
        {
            var noCode = _text["oauth.errors.noCode", TikTokName].Value;
            await _tiktok.FailConnectAsync(clientId.Value, noCode, ct);
            return RedirectWithError(noCode);
        }

        try
        {
            var redirectUri = BuildRedirectUri();
            var tokens = await _provider.ExchangeCodeAsync(code, redirectUri, ct);
            var account = await _provider.GetAccountInfoAsync(tokens.AccessToken, ct);
            await _tiktok.CompleteConnectAsync(clientId.Value, tokens, account, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TikTok OAuth code exchange failed.");
            var failed = _text["oauth.errors.connectFailed", _text.Localize(ex)].Value;
            await _tiktok.FailConnectAsync(clientId.Value, failed, ct);
            return RedirectWithError(failed);
        }

        TempData["StatusMessage"] = _text["oauth.connected", TikTokName].Value;
        return Redirect(SettingsPagePath);
    }

    private const string TikTokName = "TikTok";

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

    private record OAuthState(Guid ClientId);
}
