using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Controllers.Client;

/// <summary>
/// The UI strings for the browser: GET /i18n/{language}.js sets window.omniStrings to that language's raw texts
/// (Resources/i18n/{language}.json). The layouts load it with ?v= the file's hash, so a matching request is cached for a
/// year and a new deploy changes the URL. Public: the sign-in pages use it, and it holds no data.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class UiTextController : ControllerBase
{
    private readonly ITextCatalog _catalog;

    public UiTextController(ITextCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("/i18n/{language}.js")]
    public IActionResult Strings(string language, [FromQuery] string? v)
    {
        if (!_catalog.Supports(language)) return NotFound();
        Response.Headers.CacheControl = v == _catalog.Version(language) ? "public, max-age=31536000, immutable" : "no-cache";
        return Content(_catalog.Script(language), "text/javascript; charset=utf-8");
    }
}
