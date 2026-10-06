namespace PlasticSurgery.Entities.Responses.Inbox;

public record ConversationResponse(
    Guid Id,
    Guid ClinicId,
    Guid LeadId,
    string Channel,
    string Status,
    string Mode,
    bool AiEnabled,
    bool HumanTakeover,
    DateTimeOffset? LastMessageAt,
    string? LastMessageDirection,
    DateTimeOffset? ServiceWindowExpiresAt,
    /// <summary>Backend-computed — whether a normal free-form message can be sent right now.
    /// The Inbox uses this to decide composer-vs-template-picker, but the backend enforces it
    /// independently too (see MessageService.SendAsync) rather than trusting the client.</summary>
    bool IsServiceWindowOpen,
    DateTimeOffset CreatedAt
);
