using System.Net;
using System.Net.Sockets;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>A URL the server must not fetch (private/internal destination, bad scheme or port).</summary>
public class UnsafeUrlException : Exception
{
    public UnsafeUrlException(string message) : base(message) { }
}

/// <summary>
/// SSRF protection for the crawler — staff supply a URL and the SERVER fetches it, so this is what stops
/// someone pointing SculptFlow at its own internal network or a cloud metadata endpoint.
///
/// Two layers, both always on:
///  1. <see cref="ValidateUrlAsync"/> — before every request (including every redirect hop): http/https only,
///     ports 80/443 only, no localhost/.local/.internal names, and the host's DNS answers must ALL be public.
///  2. <see cref="ConnectAsync"/> — the HttpClient's socket connect callback re-resolves and re-checks the
///     address it is actually about to connect to, and connects to that checked IP. This closes the
///     "DNS rebinding" gap between layer 1's lookup and the real connection.
/// Blocked: loopback, private (10/8, 172.16/12, 192.168/16), link-local (169.254/16 — incl. the cloud
/// metadata IP 169.254.169.254), CGNAT, unspecified, multicast/reserved, IPv6 loopback/link-local/unique-local
/// and IPv4-mapped/6to4/Teredo/NAT64 forms of any of those. Numeric tricks (2130706433, 0x7f.1) are parsed
/// by System.Uri into a real IP before any check runs.
///
/// The one exception is <see cref="WebsiteScrapeOptions.DevAllowedHosts"/> — exact "host:port" entries that
/// are honoured ONLY in the Development environment, so tests can crawl a local fake site.
/// </summary>
public sealed class SsrfGuard
{
    private readonly HashSet<string> _devAllowed;

    public SsrfGuard(WebsiteScrapeOptions options, IHostEnvironment environment, ILogger<SsrfGuard> logger)
    {
        _devAllowed = environment.IsDevelopment()
            ? new HashSet<string>(options.DevAllowedHosts.Select(h => h.Trim().ToLowerInvariant()), StringComparer.Ordinal)
            : new HashSet<string>();
        if (_devAllowed.Count > 0)
        {
            logger.LogWarning("Website crawler: DEVELOPMENT-ONLY SSRF exemptions active for {Hosts}.", string.Join(", ", _devAllowed));
        }
    }

    private bool IsDevAllowed(string host, int port) => _devAllowed.Contains($"{host.ToLowerInvariant()}:{port}");

    public async Task ValidateUrlAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new UnsafeUrlException("Only http:// and https:// addresses can be crawled.");
        }
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new UnsafeUrlException("Addresses with a username/password aren't allowed.");
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (IsDevAllowed(host, uri.Port)) return;

        if (uri.Port != 80 && uri.Port != 443)
        {
            throw new UnsafeUrlException("Only standard web ports (80 and 443) can be crawled.");
        }
        if (IsBlockedName(host))
        {
            throw new UnsafeUrlException("That address points to an internal or private location and can't be crawled.");
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            if (IsBlockedAddress(literal)) throw Blocked();
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            throw new UnsafeUrlException($"The host '{host}' could not be found.");
        }
        if (addresses.Length == 0) throw new UnsafeUrlException($"The host '{host}' could not be found.");
        // ALL answers must be public — one private record is enough to reject (rebinding defence).
        if (addresses.Any(IsBlockedAddress)) throw Blocked();
    }

    /// <summary>SocketsHttpHandler.ConnectCallback — resolves the host, re-checks every address, and
    /// connects only to an address that passed.</summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var devAllowed = IsDevAllowed(host, port);

        IPAddress[] addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, ct);

        var usable = devAllowed ? addresses : addresses.Where(a => !IsBlockedAddress(a)).ToArray();
        if (usable.Length == 0 || (!devAllowed && usable.Length != addresses.Length))
        {
            throw new UnsafeUrlException("That address points to an internal or private location and can't be crawled.");
        }

        Exception? last = null;
        foreach (var address in usable)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException($"Could not connect to {host}.", last);
    }

    private static UnsafeUrlException Blocked() =>
        new("That address points to an internal or private location and can't be crawled.");

    private static bool IsBlockedName(string host) =>
        host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)
        || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)
        || host.EndsWith(".localdomain", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal)
        || host.EndsWith(".home.arpa", StringComparison.Ordinal) || !host.Contains('.') && !host.Contains(':');

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                          // 0.0.0.0/8  "this network"
                || b[0] == 10                                         // 10/8       private
                || (b[0] == 100 && (b[1] & 0xC0) == 64)               // 100.64/10  carrier-grade NAT
                || b[0] == 127                                        // 127/8      loopback
                || (b[0] == 169 && b[1] == 254)                       // 169.254/16 link-local + cloud metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)               // 172.16/12  private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)            // 192.0.0/24 IETF protocol assignments
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)            // TEST-NET-1
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)          // 6to4 relay
                || (b[0] == 192 && b[1] == 168)                       // 192.168/16 private
                || (b[0] == 198 && (b[1] & 0xFE) == 18)               // 198.18/15  benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)         // TEST-NET-2
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)          // TEST-NET-3
                || b[0] >= 224;                                       // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)) return true;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;                                   // fc00::/7 unique local
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return true; // 2001:db8::/32 documentation
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return true; // Teredo
            if (b[0] == 0x20 && b[1] == 0x02)                                          // 6to4: embeds an IPv4
                return IsBlockedAddress(new IPAddress(new[] { b[2], b[3], b[4], b[5] }));
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B)          // NAT64 64:ff9b::/96 embeds an IPv4
                return IsBlockedAddress(new IPAddress(new[] { b[12], b[13], b[14], b[15] }));
            return false;
        }

        return true; // unknown family: refuse
    }
}
