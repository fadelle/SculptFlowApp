using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>
/// Read-only message trace for the admin portal: one message's whole story (steps + statuses + billing + events + saved
/// errors) as a timeline, the list of recent traces, and the dashboard counts. Hours are clamped to
/// 1..MessageTraceLimits.MaxReportHours.
/// </summary>
public interface IMessageTraceAdminService
{
    /// <summary>The timeline of one trace. KeyNotFoundException (404) when nothing is known about it (or it was purged).</summary>
    Task<MessageTrace> GetAsync(string traceId, CancellationToken ct);

    /// <summary>The timeline of the trace a message was created in. KeyNotFoundException (404) when it has none.</summary>
    Task<MessageTrace> ForMessageAsync(Guid messageId, CancellationToken ct);

    /// <summary>
    /// Resolves a search: a trace id (32 hex characters), a message id (guid) or a provider message id (wamid, Telegram
    /// "chat:message"). ArgumentException (400) when empty, KeyNotFoundException (404) when nothing matches.
    /// </summary>
    Task<TraceReference> ResolveAsync(string query, CancellationToken ct);

    /// <summary>Recent traces, latest activity first; page is 1-based.</summary>
    Task<MessageTracePage> ListAsync(int hours, Guid? clinicId, Guid? conversationId, bool failedOnly, int page, int pageSize, CancellationToken ct);

    Task<MessageTraceSummary> SummaryAsync(int hours, CancellationToken ct);
}