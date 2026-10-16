using Omni.App.Entities.Dtos.PlatformAdmin;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

/// <summary>
/// Cross-clinic, read-only reads for the message trace view: a trace's steps plus what other tables already know about
/// the same messages (crm.messages statuses, billing.usage_records, activity.events, activity.app_logs).
/// </summary>
public interface IMessageTraceAdminRepository
{
    Task<List<TraceStepRow>> StepsOfTraceReadOnlyAsync(string traceId, CancellationToken ct = default);

    /// <summary>The trace a message was created in (crm.messages.trace_id), else the earliest trace with a step about it.</summary>
    Task<string?> TraceIdOfMessageReadOnlyAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>The trace of the message with this provider id (wamid, Telegram "chat:message"), if any.</summary>
    Task<string?> TraceIdOfExternalMessageReadOnlyAsync(string externalMessageId, CancellationToken ct = default);

    /// <summary>Messages created in the trace, plus the messages its steps name.</summary>
    Task<List<TraceMessageRow>> MessagesReadOnlyAsync(string traceId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    Task<List<TraceBillingRow>> BillingReadOnlyAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Business events of these conversations between <paramref name="from"/> and <paramref name="to"/>.</summary>
    Task<List<TraceEventRow>> EventsReadOnlyAsync(IReadOnlyCollection<Guid> conversationIds, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Saved warnings and errors logged in the trace.</summary>
    Task<List<AppLogRow>> LogsOfTraceReadOnlyAsync(string traceId, CancellationToken ct = default);

    /// <summary>Matching traces, latest activity first (<paramref name="skip"/>, <paramref name="take"/>), and how many match.</summary>
    Task<(List<TraceSummaryRow> Rows, int Total)> ListReadOnlyAsync(MessageTraceFilter filter, int skip, int take, CancellationToken ct = default);

    /// <summary>Traces with a step at or after <paramref name="from"/> and how many had a failed step or a saved error.</summary>
    Task<TraceCounts> CountSinceReadOnlyAsync(DateTimeOffset from, CancellationToken ct = default);
}