using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Helpers;

namespace Omni.App.Controllers.Client;

/// <summary>
/// The language pickers (header user menu, sign-in pages) post here: POST /language { language, returnUrl }. A signed-in
/// client or member saves it as their own language (empty = follow the client's default); every picker also remembers it
/// in the omni.lang cookie, which only the signed-out pages read. Then back to the page (local URLs only). A language that
/// isn't offered (no strings file, or turned off in Localization:Languages) changes nothing.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class LanguageController : ControllerBase
{
    private readonly ICurrentClientContext _currentClient;
    private readonly IMemberService _members;
    private readonly ITextContext _text;
    private readonly IConfigManager _config;

    public LanguageController(ICurrentClientContext currentClient, IMemberService members, ITextContext text, IConfigManager config)
    {
        _currentClient = currentClient;
        _members = members;
        _text = text;
        _config = config;
    }

    [HttpPost("/language")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set([FromForm] string? language, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var back = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        var code = string.IsNullOrWhiteSpace(language) ? null : language.Trim().ToLowerInvariant();
        if (code is not null && !_text.Offered.Contains(code)) return LocalRedirect(back);

        if (await _currentClient.GetAccessAsync(ct) is { } access) await _members.SetOwnLanguageAsync(access, code, ct);

        if (code is null)
            Response.Cookies.Delete(LanguagePicker.CookieName);
        else
            Response.Cookies.Append(LanguagePicker.CookieName, code, new CookieOptions
            {
                MaxAge = TimeSpan.FromDays(_config.LocalizationLanguageCookieDays), HttpOnly = true, Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax, IsEssential = true
            });
        return LocalRedirect(back);
    }
}
