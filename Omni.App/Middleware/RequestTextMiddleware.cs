using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Middleware;

/// <summary>
/// Works out the request's language and business-type words once (<see cref="ITextContext.InitializeAsync"/>), after
/// authentication so the signed-in user's client is known. Skipped for files (static assets, the /i18n/*.js strings) and
/// the SignalR hubs, which show no text.
/// </summary>
public class RequestTextMiddleware
{
    private readonly RequestDelegate _next;

    public RequestTextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITextContext text)
    {
        if (!Path.HasExtension(context.Request.Path.Value) && !context.Request.Path.StartsWithSegments("/hubs"))
        {
            await text.InitializeAsync(context.RequestAborted);
        }
        await _next(context);
    }
}
