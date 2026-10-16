using System.Text.Json;
using System.Text.Json.Serialization;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Exact shape POSTed to N8n:AiWebhookUrl — field names/casing match what n8n expects.
/// MessageText is always the clean, user-visible text regardless of how the customer replied
/// (typed, tapped a button, picked a list item) — never Meta's raw interactive JSON. SelectedValue
/// is the stable Meta reply id behind an interactive/button reply (e.g. "rhinoplasty" behind the
/// displayed "Rhinoplasty"), null for a plain typed message — see MetaWebhookParser.
/// Traceparent is the message's W3C trace ("00-{traceId}-{spanId}-01"): n8n sends it back as the traceparent header
/// on its callbacks (AI tools, the reply send, handoff), so the bot's steps join this message's trace.</summary>
public record AiTriggerPayload(
    Guid ClientId,
    Guid ConversationId,
    Guid? LeadId,
    Guid? MessageId,
    string Channel,
    string? MessageType,
    string? MessageText,
    string? SelectedValue = null,
    string? Traceparent = null
);
