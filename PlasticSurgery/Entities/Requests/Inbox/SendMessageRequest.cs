namespace PlasticSurgery.Entities.Requests.Inbox;

/// <summary>Body for POST /api/conversations/{id}/messages/send. Two callers share this one route:
/// staff from the Inbox (Sender omitted/null — unchanged behavior, flips the conversation to human
/// mode), and the AI agent via n8n (Sender = "ai" — requires the trusted X-Ingest-Key header,
/// re-checks conversation.mode == ai immediately before sending, and never flips mode). See
/// ConversationsController.SendMessage / MessageService.SendAiReplyAsync.</summary>
public record SendMessageRequest(string Content, string? MessageType = null, string? Sender = null);
