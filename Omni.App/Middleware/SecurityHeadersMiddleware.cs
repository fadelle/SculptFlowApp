namespace Omni.App.Middleware;

/// <summary>
/// Security headers on every response (QA t1-accounts-03: dashboard pages and the APIs could be put in a frame, and nothing
/// set nosniff, a referrer policy or a CSP). Set before the rest of the pipeline runs, so an endpoint can still choose its
/// own Cache-Control; antiforgery only adds X-Frame-Options when it is missing, so DENY stays.
/// <list type="bullet">
/// <item>No framing at all: X-Frame-Options DENY and CSP frame-ancestors 'none' (clickjacking).</item>
/// <item>X-Content-Type-Options nosniff, Referrer-Policy strict-origin-when-cross-origin, a Permissions-Policy that turns off
/// camera, microphone, location, payment and USB.</item>
/// <item>A Content-Security-Policy that allows the app itself, the SignalR client from cdn.jsdelivr.net and Facebook's SDK,
/// login frame and Graph calls (Messaging Integrations). Pages still use inline scripts and a few inline handlers, so
/// 'unsafe-inline' stays until they move to files; no plugins, no foreign base URL, forms post only to the app.</item>
/// <item>/api answers are never stored by a browser or proxy (Cache-Control no-store) unless the endpoint says otherwise.</item>
/// </list>
/// HSTS (a year, subdomains included) is set by UseHsts outside Development.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://connect.facebook.net; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self' wss: https://graph.facebook.com https://*.facebook.com; " +
        "frame-src https://www.facebook.com https://web.facebook.com https://*.facebook.com; " +
        "media-src 'self' blob: https:; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XFrameOptions = "DENY";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
        return _next(context);
    }
}
