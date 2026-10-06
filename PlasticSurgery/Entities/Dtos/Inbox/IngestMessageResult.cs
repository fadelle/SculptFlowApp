using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Inbox;

namespace PlasticSurgery.Entities.Dtos.Inbox;

/// <summary>Result of an ingest call — Message is null for a status_update (nothing to return) or
/// when the referenced message for a status_update couldn't be found (Found = false).
///
/// AiEligible is the concrete answer to "should n8n also call the AI agent for this?" — computed
/// here, not left for n8n to (re-)derive, per the "n8n holds no business state" principle. True
/// only for a customer_message/business-originated interactive reply, in a conversation currently
/// in AI mode, with a message type the AI is allowed to see (text/interactive) — never for media,
/// location, contact, reactions, echoes, AI's own messages, or status updates.</summary>
public record IngestMessageResult(bool Found, bool Deduplicated, MessageResponse? Message, string? ConversationMode = null, bool AiEligible = false);
