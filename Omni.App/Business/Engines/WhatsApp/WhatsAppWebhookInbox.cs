using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Omni.App.Business.Contracts.Engines.WhatsApp;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Engines.WhatsApp;

/// <inheritdoc cref="IWhatsAppWebhookInbox"/>
public class WhatsAppWebhookInbox : IWhatsAppWebhookInbox
{
    private const string Channel = "whatsapp";

    /// <summary>How long a claimed delivery stays with one worker before another may take it (a crashed worker's).</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(5);

    private readonly IWhatsAppDeliveryRepository _deliveries;
    private readonly IMetaWebhookProcessor _processor;
    private readonly IAiTriggerNotifier _aiTrigger;
    private readonly IWhatsAppDeliverySignal _signal;
    private readonly IMessageTracer _tracer;
    private readonly TimeProvider _time;
    private readonly ILogger<WhatsAppWebhookInbox> _logger;

    public WhatsAppWebhookInbox(IWhatsAppDeliveryRepository deliveries, IMetaWebhookProcessor processor, IAiTriggerNotifier aiTrigger,
        IWhatsAppDeliverySignal signal, IMessageTracer tracer, TimeProvider time, ILogger<WhatsAppWebhookInbox> logger)
    {
        _deliveries = deliveries;
        _processor = processor;
        _aiTrigger = aiTrigger;
        _signal = signal;
        _tracer = tracer;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> EnqueueAsync(byte[] body, CancellationToken ct = default)
    {
        var hash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var stored = await _deliveries.EnqueueAsync(Encoding.UTF8.GetString(body), hash, TraceContext.CurrentTraceparent(), _time.GetUtcNow(), ct);
        if (stored) _signal.Notify();
        else _tracer.Step(TraceSteps.WebhookIgnored, TraceOutcomes.Skipped, new TraceIds(Channel: Channel), detail: new { reason = "redelivered" });
        return stored;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var delivery = await _deliveries.ClaimNextAsync(now, now + ClaimLease, ct);
        if (delivery is null) return false;

        using var trace = TraceContext.Continue(delivery.TraceParent, "whatsapp.webhook");
        try
        {
            using var document = JsonDocument.Parse(delivery.Body);
            var results = await _processor.ProcessAllAsync(document.RootElement, ct);
            await _deliveries.CompleteAsync(delivery.Id, _time.GetUtcNow(), ct);
            await TriggerAiAsync(results, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The whole delivery failed (the database was down, say): try again later, then give up and keep it for a person.
            var error = TraceText.Error(ex);
            if (delivery.Attempts >= IWhatsAppWebhookInbox.MaxAttempts)
            {
                _logger.LogError(ex, "WhatsApp delivery {Id} failed {Attempts} times and was given up.", delivery.Id, delivery.Attempts);
                await _deliveries.FailAsync(delivery.Id, error, _time.GetUtcNow(), CancellationToken.None);
            }
            else
            {
                _logger.LogWarning(ex, "WhatsApp delivery {Id} failed (attempt {Attempts}); trying again.", delivery.Id, delivery.Attempts);
                await _deliveries.RetryLaterAsync(delivery.Id, error, _time.GetUtcNow() + RetryDelay(delivery.Attempts), CancellationToken.None);
            }
            _tracer.Step(TraceSteps.WebhookEventFailed, TraceOutcomes.Failed, new TraceIds(Channel: Channel),
                detail: new { kind = "delivery", attempt = delivery.Attempts, error });
        }
        return true;
    }

    /// <summary>10 s, 30 s, 90 s, 270 s after the 1st..4th failed attempt.</summary>
    public static TimeSpan RetryDelay(int attempts) => TimeSpan.FromSeconds(10 * Math.Pow(3, Math.Max(0, attempts - 1)));

    public Task<int> CleanupAsync(TimeSpan age, CancellationToken ct = default) =>
        _deliveries.DeleteDoneBeforeAsync(_time.GetUtcNow() - age, ct);

    /// <summary>One AI run per conversation (the last message wins); one failing trigger doesn't stop the others, and its message
    /// stays unanswered so a repeat asks again (QA t10-03).</summary>
    private async Task TriggerAiAsync(IReadOnlyList<WhatsAppWebhookResponse> results, CancellationToken ct)
    {
        var triggers = results
            .Where(r => r.ShouldRunAi && r.ClientId.HasValue && r.ConversationId.HasValue)
            .GroupBy(r => r.ConversationId!.Value)
            .Select(g => g.Last());
        foreach (var result in triggers)
        {
            try
            {
                await _aiTrigger.NotifyAsync(new AiTriggerPayload(
                    result.ClientId!.Value, result.ConversationId!.Value, result.ContactId, result.MessageId,
                    Channel: Channel, MessageType: result.MessageType, MessageText: result.Content,
                    SelectedValue: result.SelectedValue, AdContext: result.AdContext), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "WhatsApp webhook: the AI trigger for conversation {ConversationId} failed.", result.ConversationId);
            }
        }
    }
}

/// <inheritdoc cref="IWhatsAppDeliverySignal"/>
public sealed class WhatsAppDeliverySignal : IWhatsAppDeliverySignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
