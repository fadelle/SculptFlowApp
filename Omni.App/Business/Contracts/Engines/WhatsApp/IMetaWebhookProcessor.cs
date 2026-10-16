using System.Text.Json;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Contracts.Engines.WhatsApp;

/// <summary>
/// The single orchestration point for POST /api/integrations/whatsapp/webhook — the only thing
/// Controllers/Integrations/WhatsAppIntegrationEventsController.cs's Webhook action calls. Parses the raw Meta
/// payload (MetaWebhookParser), resolves the trusted client for each contained event from the
/// client's own stored WhatsApp connection (never from n8n or the Meta body), dispatches to the
/// matching Handler — each of which reuses the same IMessageService/IWhatsAppTemplateService/
/// IWhatsAppHealthService methods the earlier normalized per-domain endpoints called, so
/// persistence/SignalR logic exists in exactly one place regardless of how the event arrived — and
/// returns the ONE flat response n8n needs (see WhatsAppWebhookResponse's own doc comment for the
/// multi-event selection rule).
/// </summary>
public interface IMetaWebhookProcessor
{
    Task<WhatsAppWebhookResponse> ProcessAsync(JsonElement root, CancellationToken ct = default);

    /// <summary>Every event's response, in payload order (one batch can carry messages for several conversations).</summary>
    Task<IReadOnlyList<WhatsAppWebhookResponse>> ProcessAllAsync(JsonElement root, CancellationToken ct = default);
}
