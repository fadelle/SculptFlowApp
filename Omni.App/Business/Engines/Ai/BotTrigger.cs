using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Jobs;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.HttpClients.N8n;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Business.Engines.Ai;

/// <summary>
/// The AI trigger (IAiTriggerNotifier, used by the WhatsApp and Telegram webhooks and the bot test panel). The Ai / BotEngine
/// setting, read for every message, picks the engine:
/// <list type="bullet">
/// <item>n8n: AiTriggerNotifier, unchanged (its checks, then the post to N8n:AiWebhookUrl).</item>
/// <item>code: the same checks and trace steps AiTriggerNotifier makes — the plan includes the AI assistant, the account's bot
/// answers (IInboxSettingsService.ResolveAsync), the client exists — and the same trigger (the client's profile, the
/// contact's language, the message's traceparent), queued for BotRunWorker (BotEngine) instead of posted.</item>
/// </list>
/// Returns at once on the code path; never throws.
/// </summary>
public class BotTrigger : IAiTriggerNotifier
{
    private readonly ILogger<BotTrigger> _logger;
    private readonly IEntitlementService _entitlements;
    private readonly IAiProfileService _profiles;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IBotRuntimeService _bots;
    private readonly IBotRunQueue _queue;
    private readonly IMessageTracer _tracer;
    private readonly IConfigManager _config;
    private readonly AiTriggerNotifier _n8n;

    public BotTrigger(ILogger<BotTrigger> logger, IEntitlementService entitlements, IAiProfileService profiles,
        IInboxSettingsService inboxSettings, IBotRuntimeService bots, IBotRunQueue queue, IMessageTracer tracer, IConfigManager config,
        AiTriggerNotifier n8n)
    {
        _config = config;
        _n8n = n8n;
        _logger = logger;
        _entitlements = entitlements;
        _profiles = profiles;
        _inboxSettings = inboxSettings;
        _bots = bots;
        _queue = queue;
        _tracer = tracer;
    }

    public async Task NotifyAsync(AiTriggerPayload payload, CancellationToken ct = default)
    {
        if (!UsesCode)
        {
            await _n8n.NotifyAsync(payload, ct);
            return;
        }

        var ids = new TraceIds(payload.ClientId, payload.ConversationId, payload.MessageId, payload.Channel);
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

            // The bot of the account behind the conversation's channel answers, with its own configuration.
            var decision = await _inboxSettings.ResolveAsync(payload.ClientId, payload.Channel, ct);
            var bot = decision is { BotAnswers: true, BotId: { } botId } ? await _bots.ConfigAsync(payload.ClientId, botId, ct) : null;
            if (bot is null)
            {
                _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason = decision.BlockReason ?? "no_bot" });
                return;
            }

            await EnqueueAsync(payload with { Bot = bot }, ids, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare the AI trigger for conversation {ConversationId}.", payload.ConversationId);
            _tracer.Step(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed, ids, detail: new { error = TraceText.Error(ex) });
        }
    }

    public async Task<bool> NotifyTestAsync(AiTriggerPayload payload, CancellationToken ct = default)
    {
        if (!UsesCode) return await _n8n.NotifyTestAsync(payload, ct);

        var ids = new TraceIds(payload.ClientId, Channel: payload.Channel);
        try
        {
            return await EnqueueAsync(payload, ids, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare the AI trigger for bot test session {SessionId}.", payload.TestSessionId);
            return false;
        }
    }

    // Anything but "code" (the setting only allows n8n and code) keeps n8n, the default.
    private bool UsesCode => string.Equals(_config.AiBotEngine, WorkflowEngines.Code, StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds the client's profile and queues the run. True when queued.</summary>
    private async Task<bool> EnqueueAsync(AiTriggerPayload payload, TraceIds ids, CancellationToken ct)
    {
        // The prompt is generic: the business's AI profile, words, language and timezone travel with every trigger, with the
        // contact's own language and the bot's configuration.
        var profile = await _profiles.GetForClientAsync(payload.ClientId, ct);
        if (profile is null)
        {
            _logger.LogWarning("AI trigger skipped for conversation {ConversationId}: client {ClientId} not found.",
                payload.ConversationId, payload.ClientId);
            _tracer.Step(TraceSteps.BotSkipped, TraceOutcomes.Skipped, ids, detail: new { reason = "client_not_found" });
            return false;
        }

        var trigger = payload with
        {
            Traceparent = payload.Traceparent ?? TraceContext.CurrentTraceparent(),
            ClientName = profile.ClientName,
            BusinessType = profile.BusinessType,
            Language = profile.Language,
            ContactLanguage = payload.ContactId is { } contactId && payload.TestSessionId is null
                ? await _profiles.GetContactLanguageAsync(payload.ClientId, contactId, ct)
                : null,
            Timezone = profile.Timezone,
            AiProfile = profile.AiProfile,
            Terms = profile.Terms
        };

        if (!_queue.TryEnqueue(trigger))
        {
            _logger.LogWarning("AI run not queued for conversation {ConversationId}: the bot queue is closed.", payload.ConversationId);
            _tracer.Step(TraceSteps.BotTriggerFailed, TraceOutcomes.Failed, ids, detail: new { reason = "queue_closed", engine = WorkflowEngines.Code });
            return false;
        }
        _tracer.Step(TraceSteps.BotTriggered, TraceOutcomes.Ok, ids, detail: new { botId = trigger.Bot?.Id, engine = WorkflowEngines.Code, queued = true });
        return true;
    }
}
