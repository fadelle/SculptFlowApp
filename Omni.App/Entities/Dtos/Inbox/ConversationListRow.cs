namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>One row in the Inbox's conversation list (left pane).</summary>
public record ConversationListRow(
    Guid Id,
    Guid LeadId,
    string? LeadFullName,
    string? LeadPhone,
    string? ServiceName,
    string Channel,
    string Status,
    string Mode,
    string? LastMessagePreview,
    string? LastMessageDirection,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    /// <summary>Inbound (customer) messages received since staff last opened the conversation.</summary>
    int UnreadCount = 0
);
