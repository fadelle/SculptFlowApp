using System.Globalization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;

namespace Omni.App.Middleware;

/// <summary>
/// Works out the request's language and business-type words once (<see cref="ITextContext.InitializeAsync"/>), after
/// authentication so the signed-in user's client is known, and makes the language's display culture the CurrentUICulture
/// (TextCulture formats with it; CurrentCulture stays invariant, so form binding and machine-read values never change).
/// Skipped for files (static assets, the /i18n/*.js strings) and the SignalR hubs, which show no text. Also keeps the
/// translation review switch: ?textKeys=1 / ?textKeys=0 sets or clears the omni.textkeys session cookie.
/// </summary>
public class RequestTextMiddleware
{
    private readonly RequestDelegate _next;

    public RequestTextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITextContext text)
    {
        if (!Path.HasExtension(context.Request.Path.Value) && !context.Request.Path.StartsWithSegments("/hubs"))
        {
            if (context.Request.Query.TryGetValue(LanguagePicker.TextKeysQuery, out var keys))
            {
                if (keys.ToString() == "1")
                    context.Response.Cookies.Append(LanguagePicker.TextKeysCookieName, "1",
                        new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax, IsEssential = true });
                else
                    context.Response.Cookies.Delete(LanguagePicker.TextKeysCookieName);
            }

            await text.InitializeAsync(context.RequestAborted);
            CultureInfo.CurrentUICulture = text.Culture;
        }
        await _next(context);
    }
}
