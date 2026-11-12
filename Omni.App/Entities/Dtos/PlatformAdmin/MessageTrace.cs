using Omni.App.Common.Statics;

namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One message's whole story: every step, message, delivery status, charge, business event and saved error of
/// one trace, oldest first. <see cref="HasFailure"/> = a failed step, a failed delivery or a saved error.</summary>
public record MessageTrace(
    string TraceId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    Guid? ClientId,
    List<Guid> ConversationIds,
    List<Guid> MessageIds,
    bool HasFailure,
    List<MessageTraceEntry> Entries,
    // What the trace came to and why (TraceResultRule, as on the list), so the portal can say why the bot didn't answer (QA t6-admin-28).
    string Outcome = TraceResults.Other,
    string? OutcomeReason = null);