namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>One activity.message_trace_steps row as read for the trace view (Detail is the stored JSON object).</summary>
public record TraceStepRow(
    long Id,
    string TraceId,
    DateTimeOffset OccurredAt,
    string Step,
    string Outcome,
    double? DurationMs,
    Guid? ClinicId,
    Guid? ConversationId,
    Guid? MessageId,
    string? ExternalMessageId,
    string? Channel,
    string? N8nExecutionId,
    string Detail);