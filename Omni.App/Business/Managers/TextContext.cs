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
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<TextContext> _logger;
    private IReadOnlyDictionary<string, string>? _terms;

    public TextContext(ITextCatalog catalog, ITerminology terminology, ICurrentClientContext currentClient, IHttpContextAccessor http,
        ILogger<TextContext> logger)
    {
        _catalog = catalog;
        _terminology = terminology;
        _currentClient = currentClient;
        _http = http;
        _logger = logger;
    }

    public string Language { get; private set; } = AppLanguage.English;

    public string Direction => AppLanguage.Direction(Language);

    public IReadOnlyDictionary<string, string> Terms => _terms ??= TermPlaceholders.Build(_terminology.Defaults(Language));

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var access = await _currentClient.GetAccessAsync(ct);
            var client = await _currentClient.GetClientAsync(ct);
            var acceptLanguage = _http.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            // The user's own language first, then the client's default, then the browser's.
            Language = LanguagePicker.Pick([access?.Language, client?.DefaultLanguage], acceptLanguage, _catalog.Languages);
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
}
