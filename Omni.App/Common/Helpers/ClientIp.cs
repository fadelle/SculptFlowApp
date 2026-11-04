using System.Net;

namespace Omni.App.Common.Helpers;

/// <summary>
/// The visitor's IP address for per-visitor limits. Behind Render the connection (even after UseForwardedHeaders) is the
/// last proxy hop, which changes from request to request, and X-Forwarded-For's first entry can be written by the client;
/// the edge's own client-IP header can't (Cloudflare overwrites CF-Connecting-IP). So: that header when it is configured
/// (<c>Proxy:ClientIpHeader</c>) and holds an address, otherwise the connection's address.
/// </summary>
public static class ClientIp
{
    public static string Resolve(HttpContext context, string? trustedHeader)
    {
        if (!string.IsNullOrWhiteSpace(trustedHeader)
            && context.Request.Headers.TryGetValue(trustedHeader, out var values)
            && values.ToString().Split(',')[0].Trim() is { Length: > 0 } first
            && IPAddress.TryParse(first, out var address))
        {
            return address.ToString();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
