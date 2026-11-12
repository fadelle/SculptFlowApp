using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;

namespace Omni.App.Middleware;

/// <summary>
/// Once per app start, on the first request from outside: which client-IP headers the edge sends, and whether the one the
/// per-address limits trust (<c>Proxy:ClientIpHeader</c>) is among them (QA t12-fix-04: Render doesn't document
/// CF-Connecting-IP; without it every limit falls back to the changing proxy address). Logs header names only, never
/// addresses. Warns when the configured header is missing, so the deploy runbook's check is one look at the logs.
/// </summary>
public sealed class ClientIpHeaderProbeMiddleware
{
    private static readonly string[] Candidates = ["CF-Connecting-IP", "True-Client-IP", "X-Real-IP", "X-Forwarded-For", "Forwarded"];
    private static int _done;

    private readonly RequestDelegate _next;
    private readonly ILogger<ClientIpHeaderProbeMiddleware> _logger;
    private readonly string? _configured;

    public ClientIpHeaderProbeMiddleware(RequestDelegate next, ILogger<ClientIpHeaderProbeMiddleware> logger, IOptions<ProxyOptions> proxy)
    {
        _next = next;
        _logger = logger;
        _configured = proxy.Value.ClientIpHeader;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Health checks from the platform itself come without the edge's headers; wait for a real visitor.
        if (Volatile.Read(ref _done) == 0 && context.Request.Path.StartsWithSegments("/health") == false
            && Interlocked.Exchange(ref _done, 1) == 0)
        {
            Probe(context.Request.Headers);
        }
        return _next(context);
    }

    internal void Probe(IHeaderDictionary headers)
    {
        var present = Candidates.Where(h => headers.ContainsKey(h)).ToList();
        if (!string.IsNullOrWhiteSpace(_configured) && !headers.ContainsKey(_configured))
        {
            _logger.LogWarning("Client IP: the configured Proxy:ClientIpHeader {Header} isn't on requests; the edge sends {Present}. " +
                "Per-address limits fall back to the proxy's address until Proxy:ClientIpHeader names one of them.",
                _configured, present.Count == 0 ? "none of the usual headers" : string.Join(", ", present));
            return;
        }
        _logger.LogInformation("Client IP: requests carry {Present}; per-address limits use {Header}.",
            present.Count == 0 ? "none of the usual headers" : string.Join(", ", present),
            string.IsNullOrWhiteSpace(_configured) ? "the connection's address" : _configured);
    }

    /// <summary>For tests: probe again on the next request.</summary>
    internal static void Reset() => Interlocked.Exchange(ref _done, 0);
}
