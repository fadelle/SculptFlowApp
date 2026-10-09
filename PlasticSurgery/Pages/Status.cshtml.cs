using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PlasticSurgery.Pages;

/// <summary>
/// The page shown for an error status that has no body (404, 400…), re-executed by UseStatusCodePagesWithReExecute in
/// Program.cs (not for /api, /hubs or static files). Anonymous (Program.cs conventions) and antiforgery-free, so it also
/// renders for signed-out visitors and failed POSTs.
/// </summary>
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class StatusModel : PageModel
{
    public int Code { get; private set; }

    public void OnGet(int code) => Load(code);

    public void OnPost(int code) => Load(code);

    private void Load(int code)
    {
        Code = code is >= 400 and <= 599 ? code : 404;
        // A direct visit to /Status answers 200; keep the status the page describes.
        if (HttpContext.Features.Get<IStatusCodeReExecuteFeature>() is null) Response.StatusCode = Code;
    }
}
