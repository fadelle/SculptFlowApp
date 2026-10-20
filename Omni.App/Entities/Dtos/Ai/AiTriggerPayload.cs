using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Exact shape POSTed to N8n:AiWebhookUrl — field names/casing match what the n8n AI assistant workflow
/// (docs/n8n/omni-ai-assistant.json) reads, and the field list in docs/system-design.html §7 (N8nContractTests checks both).
/// MessageText is always the clean, user-visible text regardless of how the contact replied
/// (typed, tapped a button, picked a list item) — never Meta's raw interactive JSON. SelectedValue
/// is the stable Meta reply id behind an interactive/button reply (e.g. "haircut" behind the
/// displayed "Haircut"), null for a plain typed message — see MetaWebhookParser.
/// Traceparent is the message's W3C trace ("00-{traceId}-{spanId}-01"): n8n sends it back as the traceparent header
/// on its callbacks (AI tools, the reply send, handoff), so the bot's steps join this message's trace.
/// The callers fill the message fields; AiTriggerNotifier adds the client's profile (ClientName … Terms, see
/// AiClientProfile) just before sending, so the workflow's system prompt stays generic and reads the business's AI
/// profile, words, default language and timezone from here. ContactLanguage is the contact's stored language (two-letter
/// code, null when not known yet): the AI replies in the language of the message itself and falls back to it, then to
/// Language, when the message has none (an emoji, a photo).</summary>
public record AiTriggerPayload(
    Guid ClientId,
    Guid ConversationId,
    Guid? ContactId,
    Guid? MessageId,
    string Channel,
    string? MessageType,
    string? MessageText,
    string? SelectedValue = null,
    string? Traceparent = null,
    string? ClientName = null,
    string? BusinessType = null,
    string? Language = null,
    string? ContactLanguage = null,
    string? Timezone = null,
    string? AiProfile = null,
    IReadOnlyDictionary<string, TermForms>? Terms = null
);
