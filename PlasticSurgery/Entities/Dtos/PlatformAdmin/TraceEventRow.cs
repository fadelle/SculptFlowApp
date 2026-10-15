namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>A business event (activity.events) of a conversation in a trace, inside the trace's time window.</summary>
public record TraceEventRow(DateTimeOffset CreatedAt, string EventType, string? Source, Guid? ConversationId, string Metadata);