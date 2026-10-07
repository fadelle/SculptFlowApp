using System.Net;
using System.Text;
using PlasticSurgery.Business.Contracts.HttpClients.WebScraping;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Engines.WebScraping;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.WebScraping;

namespace PlasticSurgery.Business.HttpClients.WebScraping;

public sealed class WebsiteFetchClient : IWebsiteFetchClient
{
    private readonly HttpClient _http;
    private readonly SsrfGuard _guard;
    private readonly IConfigManager _config;
    private readonly ILogger<WebsiteFetchClient> _logger;

    public WebsiteFetchClient(HttpClient http, SsrfGuard guard, IConfigManager config, ILogger<WebsiteFetchClient> logger)
    {
        _http = http;
        _guard = guard;
        _config = config;
        _logger = logger;
    }

    public async Task<FetchResult> FetchPageAsync(Uri url, string? etag, string? lastModified, Func<Uri, bool> isSameSite, CancellationToken ct = default)
    {
        var current = url;
        for (var hop = 0; hop <= _config.WebScrapingMaxRedirects; hop++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_config.WebScrapingRequestTimeoutSeconds));
            try
            {
                await _guard.ValidateUrlAsync(current, timeout.Token);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(_config.WebScrapingUserAgent);
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

                if (response.Content.Headers.ContentLength is { } declared && declared > _config.WebScrapingMaxResponseBytes)
                {
                    return Fail(url, current, FetchErrorKind.TooLarge, $"Page is larger than the {_config.WebScrapingMaxResponseBytes / 1000} KB limit.", status, contentType);
                }

                var body = await ReadCappedAsync(response, timeout.Token);
                if (body is null)
                {
                    return Fail(url, current, FetchErrorKind.TooLarge, $"Page is larger than the {_config.WebScrapingMaxResponseBytes / 1000} KB limit.", status, contentType);
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
                return Fail(url, current, FetchErrorKind.Timeout, $"Timed out after {_config.WebScrapingRequestTimeoutSeconds} seconds.");
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
        return Fail(url, current, FetchErrorKind.TooManyRedirects, $"More than {_config.WebScrapingMaxRedirects} redirects.");
    }

    public async Task<RobotsFetchResult> FetchRobotsAsync(Uri origin, CancellationToken ct = default)
    {
        var robotsUrl = new Uri(origin, "/robots.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_config.WebScrapingRequestTimeoutSeconds));
        try
        {
            await _guard.ValidateUrlAsync(robotsUrl, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
            request.Headers.UserAgent.ParseAdd(_config.WebScrapingUserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;

            if (status >= 500) return new RobotsFetchResult(null, Unavailable: true, $"robots.txt returned HTTP {status}.");
            if (!response.IsSuccessStatusCode) return new RobotsFetchResult(RobotsTxt.AllowAll, false, null); // 3xx/4xx: no rules published

            var body = await ReadCappedAsync(response, timeout.Token, 512_000) ?? Array.Empty<byte>();
            var token = _config.WebScrapingUserAgent.Split('/')[0];
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
        var limit = cap ?? _config.WebScrapingMaxResponseBytes;
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
