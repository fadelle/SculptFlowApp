using System.Globalization;
using Microsoft.AspNetCore.Http;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Managers;

public class TextContext : ITextContext
{
    private readonly ITextCatalog _catalog;
    private readonly ITerminology _terminology;
    private readonly ICurrentClientContext _currentClient;
    private readonly IConfigManager _config;
    private readonly IHostEnvironment _environment;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<TextContext> _logger;
    private IReadOnlyDictionary<string, string>? _terms;
    private IReadOnlyList<string>? _offered;

    public TextContext(ITextCatalog catalog, ITerminology terminology, ICurrentClientContext currentClient, IConfigManager config,
        IHostEnvironment environment, IHttpContextAccessor http, ILogger<TextContext> logger)
    {
        _catalog = catalog;
        _terminology = terminology;
        _currentClient = currentClient;
        _config = config;
        _environment = environment;
        _http = http;
        _logger = logger;
    }

    public string Language { get; private set; } = AppLanguage.English;

    public string Direction => AppLanguage.Direction(Language);

    public CultureInfo Culture => AppCulture.For(Language);

    public IReadOnlyDictionary<string, string> Terms => _terms ??= TermPlaceholders.Build(_terminology.Defaults(Language));

    public IReadOnlyList<string> Offered => _offered ??= OfferedLanguages(_catalog, _config);

    public bool ShowKeys { get; private set; }

    /// <summary>Languages with a strings file that the Localization:Languages setting lists, English first.</summary>
    public static IReadOnlyList<string> OfferedLanguages(ITextCatalog catalog, IConfigManager config)
    {
        var enabled = LanguagePicker.Enabled(config.LocalizationLanguages);
        return catalog.Languages.Where(l => l == AppLanguage.English || enabled.Contains(l)).ToList();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var request = _http.HttpContext?.Request;
        try
        {
            ShowKeys = request is not null && ReviewModeAsked(request) && _config.LocalizationShowTextKeys && !_environment.IsProduction();
            var access = await _currentClient.GetAccessAsync(ct);
            if (access is null && request?.Path.StartsWithSegments("/api") == true)
            {
                // The admin portal, n8n and provider webhooks read English, whatever headers they send.
                Language = AppLanguage.English;
                _terms = null;
                return;
            }

            var client = access is null ? null : await _currentClient.GetClientAsync(ct);
            // Signed in: the user's own language first, then the client's default. A visitor: the language picked on the
            // sign-in pages. Then the browser's, then English.
            string?[] preferred = access is null
                ? [request?.Cookies[LanguagePicker.CookieName]]
                : [access.Language, client?.DefaultLanguage];
            Language = LanguagePicker.Pick(preferred, request?.Headers.AcceptLanguage.ToString(), Offered);
            _terms = TermPlaceholders.Build(client is null
                ? _terminology.Defaults(Language)
                : await _terminology.ForBusinessTypeAsync(client.BusinessTypeCode, Language, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Words are never the reason a request fails: keep English and the default words.
            _logger.LogWarning(ex, "Couldn't work out the request's language and words; using English defaults.");
            Language = AppLanguage.English;
            _terms = null;
        }
    }

    /// <summary>?textKeys=1 asks for review mode on this request; without the query, the omni.textkeys cookie
    /// (RequestTextMiddleware sets it from the query) keeps it on.</summary>
    private static bool ReviewModeAsked(HttpRequest request) =>
        request.Query.TryGetValue(LanguagePicker.TextKeysQuery, out var asked)
            ? asked.ToString() == "1"
            : request.Cookies[LanguagePicker.TextKeysCookieName] == "1";
}
