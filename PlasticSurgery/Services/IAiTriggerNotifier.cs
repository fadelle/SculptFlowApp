using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Services;

/// <summary>
/// The single outbound call to n8n now that Meta posts directly to our webhook instead of n8n
/// relaying the raw payload (see Controllers/WhatsAppWebhookController.cs). n8n no longer sees Meta's
/// webhook shape at all — it receives only this small normalized trigger, and only for the one case
/// that's ever allowed to reach the AI: an eligible customer message in a conversation currently in
/// AI mode (MetaWebhookProcessor/WhatsAppWebhookResponse.ShouldRunAi already decided that).
/// </summary>
public interface IAiTriggerNotifier
{
    Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default);
}

/// <summary>Exact shape POSTed to N8n:AiWebhookUrl — field names/casing match what n8n expects.
/// MessageText is always the clean, user-visible text regardless of how the customer replied
/// (typed, tapped a button, picked a list item) — never Meta's raw interactive JSON. SelectedValue
/// is the stable Meta reply id behind an interactive/button reply (e.g. "rhinoplasty" behind the
/// displayed "Rhinoplasty"), null for a plain typed message — see MetaWebhookParser.</summary>
public record AiTriggerPayload(
    Guid ClinicId,
    Guid ConversationId,
    Guid? LeadId,
    Guid? MessageId,
    string Channel,
    string? MessageType,
    string? MessageText,
    string? SelectedValue = null
);

public class AiTriggerNotifier : IAiTriggerNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiTriggerNotifier> _logger;

    public AiTriggerNotifier(HttpClient http, IConfiguration configuration, ILogger<AiTriggerNotifier> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Never throws — a webhook POST from Meta must still get its fast HTTP 200 even if
    /// n8n is unreachable or misconfigured; this logs and swallows instead of failing the caller.
    /// Idempotency isn't a concern here the way it is for persistence: at worst a flaky n8n call
    /// means one AI turn doesn't fire, which is the same as the customer needing to send one more
    /// message — not a data-correctness problem.</summary>
    public async Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default)
    {
        var url = _configuration["N8n:AiWebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("N8n:AiWebhookUrl is not configured — skipping AI trigger for conversation {ConversationId}.", payload.ConversationId);
            return;
        }

        try
        {
            using var response = await _http.PostAsJsonAsync(url, payload, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("n8n AI webhook returned {StatusCode} for conversation {ConversationId}: {Body}",
                    (int)response.StatusCode, payload.ConversationId, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call n8n AI webhook for conversation {ConversationId}.", payload.ConversationId);
        }
    }
}
