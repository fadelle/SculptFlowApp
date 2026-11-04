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
    Task<MessageTracePage> ListAsync(int hours, Guid? clientId, Guid? conversationId, bool failedOnly, int page, int pageSize, CancellationToken ct);

    Task<MessageTraceSummary> SummaryAsync(int hours, CancellationToken ct);

    /// <summary>Conversations whose last message is the customer's and got no answer within <paramref name="minutes"/>
    /// (1..1440, default 15), sent in the last <paramref name="hours"/>, with what that message's trace came to (QA t7-10).</summary>
    Task<UnansweredPage> UnansweredAsync(int hours, int minutes, Guid? clientId, int page, int pageSize, CancellationToken ct);

    /// <summary>AI bot use per client over the last <paramref name="days"/> (1..90): runs, model calls, tokens, the estimated
    /// model cost and the ai_reply revenue (QA t7-10).</summary>
    Task<AiUsageReport> AiUsageAsync(int days, Guid? clientId, CancellationToken ct);
}