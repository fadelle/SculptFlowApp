namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>
/// The admin portal's Errors page: problems logged since <see cref="From"/> at <see cref="Level"/> or above, sorted by
/// <see cref="Sort"/> ("recent" or "count"), the first <c>take</c> of them in <see cref="Groups"/>. Totals cover every
/// matching group. The ErrorLog settings are included so the page can say what is being captured.
/// </summary>
public record AppLogGroupReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int Hours,
    string Level,
    string Sort,
    bool Enabled,
    string MinimumLevel,
    int WarningRetentionDays,
    int ErrorRetentionDays,
    long TotalOccurrences,
    int GroupCount,
    List<AppLogGroupRow> Groups);