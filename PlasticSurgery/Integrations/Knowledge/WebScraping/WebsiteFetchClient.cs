using System.Net;
using System.Text;

namespace PlasticSurgery.Integrations.Knowledge.WebScraping;

/// <summary>Why a fetch didn't produce a page. Drives whether the crawler records a failure, a skip, or a removal.</summary>
public enum FetchErrorKind
{
    None,
    /// <summary>SSRF guard refused the URL (or a redirect hop).</summary>
    Blocked,
    Timeout,
    Network,
    /// <summary>Non-success HTTP status (see StatusCode).</summary>
    HttpError,
    /// <summary>The response wasn't HTML (an image, PDF, JSON, …) — skipped, never parsed.</summary>
    NotHtml,
    TooLarge,
    TooManyRedirects,
    /// <summary>Redirected to another site — the crawler stays on the configured website.</summary>
    ExternalRedirect
}

public sealed record FetchResult
{
    public string RequestedUrl { get; init; } = string.Empty;
    /// <summary>The URL the content actually came from after redirects.</summary>
    public string FinalUrl { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public string? ContentType { get; init; }
    public string? Html { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
    public bool NotModified { get; init; }
    public FetchErrorKind ErrorKind { get; init; }
    public string? Error { get; init; }
    public bool Ok => ErrorKind == FetchErrorKind.None && (Html is not null || NotModified);
}

public sealed record RobotsFetchResult(RobotsTxt? Robots, bool Unavailable, string? Note);

/// <summary>
/// All HTTP the crawler does. Every request: SSRF-validated first, sent with the SculptFlow crawler
/// User-Agent, with a hard timeout; redirects are followed MANUALLY (never automatically) so each hop is
/// re-validated and can be refused if it leaves the configured site; responses must be HTML and are read up
/// to a size cap (after decompression). No cookies, no proxy, no credentials.
/// </summary>
public interface IWebsiteFetchClient
{
    Task<FetchResult> FetchPageAsync(Uri url, string? etag, string? lastModified, Func<Uri, bool> isSameSite, CancellationToken ct = default);
    Task<RobotsFetchResult> FetchRobotsAsync(Uri origin, CancellationToken ct = default);
}

public sealed class WebsiteFetchClient : IWebsiteFetchClient
{
    private readonly HttpClient _http;
    private readonly SsrfGuard _guard;
    private readonly WebsiteScrapeOptions _options;
    private readonly ILogger<WebsiteFetchClient> _logger;

    public WebsiteFetchClient(HttpClient http, SsrfGuard guard, WebsiteScrapeOptions options, ILogger<WebsiteFetchClient> logger)
    {
        _http = http;
        _guard = guard;
        _options = options;
        _logger = logger;
    }

    public async Task<FetchResult> FetchPageAsync(Uri url, string? etag, string? lastModified, Func<Uri, bool> isSameSite, CancellationToken ct = default)
    {
        var current = url;
        for (var hop = 0; hop <= _options.MaxRedirects; hop++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
            try
            {
                await _guard.ValidateUrlAsync(current, timeout.Token);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(_options.UserAgent);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.1");
                request.Headers.AcceptLanguage.ParseAdd("en;q=0.9,*;q=0.5");
                if (hop == 0)
                {
                    // Conditional request on the first hop only (a redirected URL is a different resource).
                    if (!string.IsNullOrEmpty(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    if (!string.IsNullOrEmpty(lastModified)) request.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = (int)response.StatusCode;

                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location;
                    if (location is null) return Fail(url, current, FetchErrorKind.HttpError, $"HTTP {status} redirect without a Location.", status);
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (!isSameSite(next))
                    {
                        return Fail(url, current, FetchErrorKind.ExternalRedirect, $"Redirects to another website ({next.Host}).", status);
                    }
                    current = next;
                    continue;
                }

                if (status == 304)
                {
                    return new FetchResult { RequestedUrl = url.ToString(), FinalUrl = current.ToString(), StatusCode = 304, NotModified = true,
                        ETag = etag, LastModified = lastModified };
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Fail(url, current, FetchErrorKind.HttpError, $"HTTP {status} {response.ReasonPhrase}".Trim(), status);
                }

                var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                if (contentType is not ("text/html" or "application/xhtml+xml"))
                {
                    return Fail(url, current, FetchErrorKind.NotHtml, $"Not an HTML page (content type: {contentType ?? "unknown"}).", status, contentType);
                }

                if (response.Content.Headers.ContentLength is { } declared && declared > _options.MaxResponseBytes)
                {
                    return Fail(url, current, FetchErrorKind.TooLarge, $"Page is larger than the {_options.MaxResponseBytes / 1000} KB limit.", status, contentType);
                }

                var body = await ReadCappedAsync(response, timeout.Token);
                if (body is null)
                {
                    return Fail(url, current, FetchErrorKind.TooLarge, $"Page is larger than the {_options.MaxResponseBytes / 1000} KB limit.", status, contentType);
                }

                return new FetchResult
                {
                    RequestedUrl = url.ToString(),
                    FinalUrl = current.ToString(),
                    StatusCode = status,
                    ContentType = contentType,
                    Html = Decode(body, response.Content.Headers.ContentType?.CharSet),
                    ETag = response.Headers.ETag?.ToString(),
                    LastModified = response.Content.Headers.LastModified?.ToString("R")
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Fail(url, current, FetchErrorKind.Timeout, $"Timed out after {_options.RequestTimeoutSeconds} seconds.");
            }
            catch (UnsafeUrlException ex)
            {
                return Fail(url, current, FetchErrorKind.Blocked, ex.Message);
            }
            catch (HttpRequestException ex)
            {
                // The connect callback throws UnsafeUrlException, which HttpClient wraps.
                for (Exception? e = ex; e is not null; e = e.InnerException)
                {
                    if (e is UnsafeUrlException blocked) return Fail(url, current, FetchErrorKind.Blocked, blocked.Message);
                }
                _logger.LogDebug(ex, "Network error fetching {Url}", current);
                return Fail(url, current, FetchErrorKind.Network, "Could not connect to the website.");
            }
        }
        return Fail(url, current, FetchErrorKind.TooManyRedirects, $"More than {_options.MaxRedirects} redirects.");
    }

    public async Task<RobotsFetchResult> FetchRobotsAsync(Uri origin, CancellationToken ct = default)
    {
        var robotsUrl = new Uri(origin, "/robots.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            await _guard.ValidateUrlAsync(robotsUrl, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;

            if (status >= 500) return new RobotsFetchResult(null, Unavailable: true, $"robots.txt returned HTTP {status}.");
            if (!response.IsSuccessStatusCode) return new RobotsFetchResult(RobotsTxt.AllowAll, false, null); // 3xx/4xx: no rules published

            var body = await ReadCappedAsync(response, timeout.Token, 512_000) ?? Array.Empty<byte>();
            var token = _options.UserAgent.Split('/')[0];
            return new RobotsFetchResult(RobotsTxt.Parse(Encoding.UTF8.GetString(body), token), false, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RobotsFetchResult(null, true, "robots.txt timed out.");
        }
        catch (Exception ex) when (ex is UnsafeUrlException or HttpRequestException)
        {
            return new RobotsFetchResult(null, true, ex is UnsafeUrlException u ? u.Message : "Could not reach the website to read robots.txt.");
        }
    }

    private async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct, int? cap = null)
    {
        var limit = cap ?? _options.MaxResponseBytes;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > limit) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"', '\'')).GetString(bytes); }
            catch (ArgumentException) { /* unknown charset: fall through to UTF-8 */ }
        }
        // UTF-8 (BOM stripped). A page declaring another charset only in <meta> is rare enough to accept mojibake.
        return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
    }

    private static FetchResult Fail(Uri requested, Uri current, FetchErrorKind kind, string message, int? status = null, string? contentType = null) =>
        new() { RequestedUrl = requested.ToString(), FinalUrl = current.ToString(), ErrorKind = kind, Error = message, StatusCode = status, ContentType = contentType };
}
