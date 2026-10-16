using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Services.Monitoring;

/// <summary>Writes and trims activity.message_trace_steps for MessageTraceFlushJob.</summary>
public interface IMessageTraceService
{
    /// <summary>Saves a batch of recorded steps (nothing when empty).</summary>
    Task SaveAsync(IReadOnlyCollection<MessageTraceStep> rows, CancellationToken ct);

    /// <summary>
    /// Deletes steps older than MessageTrace:RetentionDays; traces with a failed step are kept until
    /// MessageTrace:FailedRetentionDays. Returns rows deleted.
    /// </summary>
    Task<int> PurgeAsync(CancellationToken ct);
}