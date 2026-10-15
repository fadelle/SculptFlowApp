namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One message's whole story: every step, message, delivery status, charge, business event and saved error of
/// one trace, oldest first. <see cref="HasFailure"/> = a failed step, a failed delivery or a saved error.</summary>
public record MessageTrace(
    string TraceId,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    Guid? ClinicId,
    List<Guid> ConversationIds,
    List<Guid> MessageIds,
    bool HasFailure,
    List<MessageTraceEntry> Entries);