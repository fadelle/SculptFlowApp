using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;

namespace Omni.App.Middleware;

/// <summary>
/// Times every request (API, page) by its matched route template and records it in <see cref="IApiTimingCollector"/>
/// (memory only; ApiTimingFlushJob writes a per-minute summary). A request at or over Monitoring:SlowRequestMs is also
/// logged as a warning. Runs after UseRouting so the matched endpoint is known. Skipped while Monitoring:Enabled is off,
/// and for static files, SignalR hubs (long-lived connections) and the status-page re-execute (already counted).
/// Only the route template and method are logged or stored, never the raw path or query (they can carry ids or tokens).
/// </summary>
public sealed class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfigManager _config;
    private readonly IApiTimingCollector _collector;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, IConfigManager config, IApiTimingCollector collector,
        ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _config = config;
        _collector = collector;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_config.MonitoringEnabled || !IsTimed(context))
        {
            await _next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var threw = false;
        try
        {
            await _next(context);
        }
        catch
        {
            // Not handled here: only noted so the request counts as a 500 (UseExceptionHandler turns it into one).
            threw = true;
            throw;
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var status = threw ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var route = RouteOf(context);
            var threshold = _config.MonitoringSlowRequestMs;
            var slow = elapsedMs >= threshold;
            _collector.Record(context.Request.Method, route, status, elapsedMs, slow);
            if (slow)
            {
                _logger.LogWarning("Slow request: {Method} {Route} took {ElapsedMs:F0} ms (status {StatusCode}, threshold {ThresholdMs} ms).",
                    context.Request.Method, route, elapsedMs, status, threshold);
            }
        }
    }

    private static bool IsTimed(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/hubs")
        && !Path.HasExtension(context.Request.Path.Value)
        && context.Features.Get<IStatusCodeReExecuteFeature>() is null;

    /// <summary>The matched route template with a leading '/', e.g. "/api/platform-admin/cache/keys" or "/Contacts/Details".</summary>
    public static string RouteOf(HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint) return ApiTimingLimits.UnmatchedRoute;
        var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
        return route.Length <= ApiTimingLimits.MaxRouteLength ? route : route[..ApiTimingLimits.MaxRouteLength];
    }
}
