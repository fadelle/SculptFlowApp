using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Business.Contracts.HttpClients.N8n;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Entities.Dtos.Ai;

namespace PlasticSurgery.Business.HttpClients.N8n;

public class AiTriggerNotifier : IAiTriggerNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiTriggerNotifier> _logger;
    private readonly IEntitlementService _entitlements;

    public AiTriggerNotifier(HttpClient http, IConfiguration configuration, ILogger<AiTriggerNotifier> logger,
        IEntitlementService entitlements)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
        _entitlements = entitlements;
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
            // The AI agent is a plan feature (automation). Without it the message just waits in the Inbox for staff.
            var entitlements = await _entitlements.GetAsync(payload.ClinicId, ct);
            if (!entitlements.CanUseAiAgent)
            {
                _logger.LogInformation("AI trigger skipped for conversation {ConversationId}: the clinic's plan doesn't include the AI agent (status {Status}).",
                    payload.ConversationId, entitlements.SubscriptionStatus ?? "none");
                return;
            }

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
