namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>
/// The admin portal's API timings page: every route seen since <see cref="From"/>, sorted by <see cref="Sort"/>, the
/// first <c>take</c> of them in <see cref="Routes"/>. Totals cover all routes, not just the ones returned. Enabled and
/// SlowRequestMs are the current Monitoring settings.
/// </summary>
public record ApiTimingReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int Hours,
    string Sort,
    bool Enabled,
    int SlowRequestMs,
    int RetentionDays,
    long TotalRequests,
    long TotalErrors,
    long TotalSlowRequests,
    int RouteCount,
    List<RouteTimingRow> Routes);
