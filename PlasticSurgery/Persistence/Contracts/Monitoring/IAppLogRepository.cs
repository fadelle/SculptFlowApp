using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Monitoring;

/// <summary>activity.app_logs: saved warnings and errors (see AppLog).</summary>
public interface IAppLogRepository
{
    /// <summary>Queues the rows; they are written with the caller's next save.</summary>
    void AddRange(IEnumerable<AppLog> rows);

    /// <summary>Set-based delete of rows at one of <paramref name="levels"/> logged before <paramref name="cutoff"/>. Returns rows deleted.</summary>
    Task<int> DeleteOlderThanAsync(IReadOnlyCollection<string> levels, DateTimeOffset cutoff, CancellationToken ct = default);

    /// <summary>
    /// One row per fingerprint matching <paramref name="filter"/>, sorted by last seen ("recent") or occurrences ("count"),
    /// the first <paramref name="take"/>; plus the number of matching groups and their total occurrences.
    /// </summary>
    Task<(List<AppLogGroupRow> Groups, int GroupCount, long TotalOccurrences)> ListGroupsReadOnlyAsync(
        AppLogFilter filter, bool byCount, int take, CancellationToken ct = default);

    /// <summary>Matching rows newest first (<paramref name="skip"/>, <paramref name="take"/>) and how many match.</summary>
    Task<(List<AppLogRow> Rows, int Total)> ListReadOnlyAsync(AppLogFilter filter, int skip, int take, CancellationToken ct = default);

    Task<AppLog?> GetReadOnlyAsync(long id, CancellationToken ct = default);

    /// <summary>Occurrences per level logged at or after <paramref name="from"/> (levels with none are left out).</summary>
    Task<List<AppLogLevelCount>> CountByLevelSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default);
}
