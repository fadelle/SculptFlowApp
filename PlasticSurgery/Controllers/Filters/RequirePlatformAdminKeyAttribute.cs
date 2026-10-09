using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PlasticSurgery.Controllers.Filters;

/// <summary>
/// Service auth for the platform-admin API (/api/platform-admin/{domain}/…). The main app owns each domain's rules and
/// writes; the SculptFlowAdmin portal is the control panel that calls these endpoints server to server. Clinic staff
/// accounts have no roles in this app, so this is a shared secret: PlatformAdmin:ApiKey, sent as X-Platform-Admin-Key.
/// Compared in constant time. An empty key turns every platform-admin endpoint off (404), so a server that never
/// configured it exposes nothing. X-Admin-Actor names the admin who acted (written to the ledger / audit trail).
/// New admin domains reuse this attribute and the same conventions (actor header, Idempotency-Key on money writes).
/// </summary>
public class RequirePlatformAdminKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string ConfigKey = "PlatformAdmin:ApiKey";
    public const string HeaderName = "X-Platform-Admin-Key";
    public const string ActorHeaderName = "X-Admin-Actor";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[ConfigKey];
        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault() ?? string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
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
