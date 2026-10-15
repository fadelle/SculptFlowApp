namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>
/// One line of a message's timeline. <see cref="Kind"/>: "step" (a trace step), "message" (a message row was saved),
/// "status" (a delivery status from the message row), "billing", "event" (activity.events), "error" or "warning" (a saved
/// log row of the same trace; <see cref="LogId"/> opens it). <see cref="Outcome"/> is ok, skipped or failed.
/// </summary>
public record MessageTraceEntry(
    DateTimeOffset At,
    string Kind,
    string Name,
    string Outcome,
    string Summary,
    double? DurationMs,
    Guid? ClinicId,
    Guid? ConversationId,
    Guid? MessageId,
    string? N8nExecutionId,
    long? LogId,
    Dictionary<string, string?> Detail);