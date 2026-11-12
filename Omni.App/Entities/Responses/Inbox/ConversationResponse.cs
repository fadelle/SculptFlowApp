namespace Omni.App.Entities.Responses.Inbox;

public record ConversationResponse(
    Guid Id,
    Guid ClientId,
    Guid ContactId,
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
    DateTimeOffset CreatedAt,
    /// <summary>Whether the conversation's channel is connected (set when one conversation is opened; QA t8-18): the Inbox turns
    /// the composer off with a notice instead of letting a send fail.</summary>
    bool ChannelConnected = true
);
