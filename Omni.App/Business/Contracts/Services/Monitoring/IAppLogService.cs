using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Services.Monitoring;

/// <summary>Writes and trims activity.app_logs for AppLogFlushJob.</summary>
public interface IAppLogService
{
    /// <summary>Saves a batch of captured entries (nothing when empty).</summary>
    Task SaveAsync(IReadOnlyCollection<AppLog> rows, CancellationToken ct);

    /// <summary>
    /// Deletes warnings older than ErrorLog:WarningRetentionDays and errors/criticals older than
    /// ErrorLog:ErrorRetentionDays. Returns rows deleted.
    /// </summary>
    Task<int> PurgeAsync(CancellationToken ct);
}
