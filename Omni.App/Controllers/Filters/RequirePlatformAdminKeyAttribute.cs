using Microsoft.AspNetCore.Mvc;
using Omni.App.Common.Helpers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Service auth for the platform-admin API (/api/platform-admin/{domain}/…). The main app owns each domain's rules and
/// writes; the Omni.Admin portal is the control panel that calls these endpoints server to server. Client members
/// accounts have no roles in this app, so this is a shared secret: PlatformAdmin:ApiKey, sent as X-Platform-Admin-Key.
/// Compared in constant time. An empty key turns every platform-admin endpoint off (404), so a server that never
/// configured it exposes nothing. X-Admin-Actor names the admin who acted (written to the ledger / audit trail).
/// New admin domains reuse this attribute and the same conventions (actor header, Idempotency-Key on money writes).
/// </summary>
public class RequirePlatformAdminKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string ConfigKey = PlatformAdminKey.ConfigKey;
    public const string HeaderName = PlatformAdminKey.HeaderName;
    public const string ActorHeaderName = "X-Admin-Actor";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[ConfigKey];
        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new NotFoundResult();
            return;
        }

        if (!PlatformAdminKey.Matches(context.HttpContext.Request, context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()))
        {
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing or invalid {HeaderName} header." });
            return;
        }

        await next();
    }

    /// <summary>The acting admin from X-Admin-Actor (trimmed to 200 chars), or "admin-api" when absent.</summary>
    public static string Actor(HttpRequest request) =>
        request.Headers[ActorHeaderName].FirstOrDefault()?.Trim() is { Length: > 0 } a ? a[..Math.Min(a.Length, 200)] : "admin-api";
}
