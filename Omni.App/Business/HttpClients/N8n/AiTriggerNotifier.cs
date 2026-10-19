using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Business.HttpClients.N8n;

public class AiTriggerNotifier : IAiTriggerNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiTriggerNotifier> _logger;
    private readonly IEntitlementService _entitlements;
    private readonly IAiProfileService _profiles;
    private readonly IMessageTracer _tracer;

    public AiTriggerNotifier(HttpClient http, IConfiguration configuration, ILogger<AiTriggerNotifier> logger,
        IEntitlementService entitlements, IAiProfileService profiles, IMessageTracer tracer)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
        _entitlements = entitlements;
        _profiles = profiles;
        _tracer = tracer;
    }

    /// <summary>Never throws — a webhook POST from Meta must still get its fast HTTP 200 even if
    /// n8n is unreachable or misconfigured; this logs and swallows instead of failing the caller.
    /// Idempotency isn't a concern here the way it is for persistence: at worst a flaky n8n call
    /// means one AI turn doesn't fire, which is the same as the customer needing to send one more
    /// message — not a data-correctness problem. Every outcome is a step in the message trace.</summary>
    public async Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default)
    {
        var ids = new TraceIds(payload.ClientId, payload.ConversationId, payload.MessageId, payload.Channel);
        var url = _configuration["N8n:AiWebhookUrl"];
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("N8n:AiWebhookUrl is not configured — skipping AI trigger for conversation {ConversationId}.", payload.ConversationId);
            _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason = "n8n_not_configured" });
            return;
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            // The AI assistant is a plan feature (automation). Without it the message just waits in the Inbox for members.
            var entitlements = await _entitlements.GetAsync(payload.ClientId, ct);
            if (!entitlements.CanUseAiAssistant)
            {
                _logger.LogInformation("AI trigger skipped for conversation {ConversationId}: the client's plan doesn't include the AI assistant (status {Status}).",
                    payload.ConversationId, entitlements.SubscriptionStatus ?? "none");
                _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason = "plan_without_ai_assistant" });
                return;
            }

            // The workflow's prompt is generic: the business's AI profile, words, language and timezone travel with every trigger.
            var profile = await _profiles.GetForClientAsync(payload.ClientId, ct);
            if (profile is null)
            {
                _logger.LogWarning("AI trigger skipped for conversation {ConversationId}: client {ClientId} not found.",
                    payload.ConversationId, payload.ClientId);
                _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason = "client_not_found" });
                return;
            }

            var trigger = payload with
            {
                Traceparent = payload.Traceparent ?? TraceContext.CurrentTraceparent(),
                ClientName = profile.ClientName,
                BusinessType = profile.BusinessType,
                Language = profile.Language,
                Timezone = profile.Timezone,
                AiProfile = profile.AiProfile,
                Terms = profile.Terms
            };
            using var response = await _http.PostAsJsonAsync(url, trigger, JsonOptions, ct);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("n8n AI webhook returned {StatusCode} for conversation {ConversationId}: {Body}",
                    (int)response.StatusCode, payload.ConversationId, body);
                _tracer.Step(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed, ids, elapsed, new { status = (int)response.StatusCode });
                return;
            }
            _tracer.Step(TraceSteps.BotTriggered, TraceOutcomes.Ok, ids, elapsed, new { status = (int)response.StatusCode });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call n8n AI webhook for conversation {ConversationId}.", payload.ConversationId);
            _tracer.Step(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed, ids, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new { error = TraceText.Error(ex) });
        }
    }
}
