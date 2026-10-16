namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A message in a trace with its delivery lifecycle (crm.messages; no content: the trace is about the path).</summary>
public record TraceMessageRow(
    Guid Id,
    Guid ClientId,
    Guid ConversationId,
    string Direction,
    string SenderType,
    string Origin,
    string Channel,
    string MessageType,
    string? ExternalMessageId,
    string? DeliveryStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? FailedAt,
    string? FailureCode,
    string? FailureReason,
    string? TraceId);