using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Monitoring;

/// <summary>activity.message_trace_steps: writes and trimming (the platform-admin reads are in IMessageTraceAdminRepository).</summary>
public interface IMessageTraceStepRepository
{
    /// <summary>Queues the rows; they are written with the caller's next save.</summary>
    void AddRange(IEnumerable<MessageTraceStep> rows);

    /// <summary>
    /// Set-based delete of steps older than <paramref name="cutoff"/>, except steps of a trace that has a failed step,
    /// which are kept until <paramref name="failedCutoff"/>. Returns rows deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, DateTimeOffset failedCutoff, CancellationToken ct = default);
}