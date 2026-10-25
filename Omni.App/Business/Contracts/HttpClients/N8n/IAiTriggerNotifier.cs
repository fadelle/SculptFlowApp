using System.Text.Json;
using System.Text.Json.Serialization;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.HttpClients.N8n;

/// <summary>
/// The single outbound call to n8n now that Meta posts directly to our webhook instead of n8n
/// relaying the raw payload (see Controllers/Integrations/WhatsAppWebhookController.cs). n8n no longer sees Meta's
/// webhook shape at all — it receives only this small normalized trigger, and only for the one case
/// that's ever allowed to reach the AI: an eligible customer message in a conversation currently in
/// AI mode (MetaWebhookProcessor/WhatsAppWebhookResponse.ShouldRunAi already decided that).
/// </summary>
public interface IAiTriggerNotifier
{
    /// <summary>A contact's message: adds the bot of the account the conversation's channel belongs to
    /// (IInboxSettingsService.ResolveAsync) and the client's profile, then posts. Skipped (traced) when no bot answers there.
    /// Never throws.</summary>
    Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default);

    /// <summary>A bot test conversation's message (payload with Bot and TestSessionId already set): adds the client's profile
    /// and posts. False when n8n isn't configured or didn't accept it. Never throws.</summary>
    Task<bool> NotifyTestAsync(AiTriggerPayload payload, CancellationToken ct = default);
}
