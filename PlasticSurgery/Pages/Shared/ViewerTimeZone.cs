using Microsoft.AspNetCore.Http;

namespace PlasticSurgery.Pages.Shared;

/// <summary>
/// The timezone of the person looking at the page. wwwroot/js/local-time.js stores the browser's IANA zone in the
/// <c>sf-tz</c> cookie on every page load, so server-side day math (calendar grid, date ranges, typed dates/times)
/// matches what that viewer's own clock says. Only Clinic Info → Availability works in the clinic's timezone.
/// </summary>
public static class ViewerTimeZone
{
    public const string CookieName = "sf-tz";

    /// <summary>The viewer's timezone from the cookie; <paramref name="fallbackId"/> (normally the clinic's) until the
    /// browser has set it, then UTC if neither is a known zone.</summary>
    public static TimeZoneInfo Resolve(HttpRequest? request, string? fallbackId = null) =>
        Find(request?.Cookies[CookieName]) ?? Find(fallbackId) ?? TimeZoneInfo.Utc;

    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    /// <summary>Wall-clock <paramref name="local"/> in <paramref name="tz"/>, as UTC.</summary>
    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }
}
