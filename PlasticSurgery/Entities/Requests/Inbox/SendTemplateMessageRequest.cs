namespace PlasticSurgery.Entities.Requests.Inbox;

/// <summary>Body for POST /api/conversations/{id}/messages/send-template — a staff-initiated
/// template send from the Inbox (Origin stays Dashboard; MessageType becomes "template"). Used
/// when the service window is closed and free-form sending isn't allowed, but also usable any
/// time staff wants to proactively reach out (reminders, follow-ups, etc.) with an approved
/// template. BodyParameters are positional {{1}}, {{2}}... substitutions, in order.</summary>
public record SendTemplateMessageRequest(Guid WhatsAppTemplateId, IReadOnlyList<string>? BodyParameters);
