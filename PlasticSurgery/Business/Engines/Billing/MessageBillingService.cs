using PlasticSurgery.Business.Contracts.Engines.Billing;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Billing;

namespace PlasticSurgery.Business.Engines.Billing;

public class MessageBillingService : IMessageBillingService
{
    private readonly IBillingService _billing;
    private readonly IEnumerable<IChannelBillingPolicy> _policies;
    private readonly IProviderBillingService _providerBilling;
    private readonly IBillingUnitOfWorkFactory _units;
    private readonly TimeProvider _time;
    private readonly ILogger<MessageBillingService> _logger;
    private readonly IConfigManager _config;

    public MessageBillingService(IBillingService billing, IEnumerable<IChannelBillingPolicy> policies, IProviderBillingService providerBilling,
        IBillingUnitOfWorkFactory units, TimeProvider time, ILogger<MessageBillingService> logger, IConfigManager config)
    {
        _config = config;
        _billing = billing;
        _policies = policies;
        _providerBilling = providerBilling;
        _units = units;
        _time = time;
        _logger = logger;
    }

    public async Task<MessageBillingHold?> ReserveOutboundAsync(OutboundMessageBillingContext message, CancellationToken ct = default)
    {
        if (!_config.BillingEnabled) return null;

        var policy = PolicyFor(message.Channel);
        if (policy is null)
        {
            _logger.LogWarning("Billing: no billing policy for channel {Channel}; message {MessageId} is not billed.", message.Channel, message.MessageId);
            return null;
        }

        var decision = policy.DescribeOutbound(message);
        if (decision is null) return null; // nothing worth recording (e.g. a WhatsApp reply inside the service window)

        // Who pays the provider, and does SculptFlow charge, is the connected account's arrangement — not the channel's.
        var arrangement = await _providerBilling.ResolveAsync(message.ClinicId, message.Channel, decision.Provider, ct);
        var key = MessageBillingKeys.For(message.Channel, message.MessageId);
        var billableEvent = new BillableEvent
        {
            ClinicId = message.ClinicId,
            IdempotencyKey = key,
            EventType = decision.EventType,
            Channel = message.Channel,
            Quantity = decision.Quantity,
            Unit = decision.Unit,
            CountryCode = decision.CountryCode,
            Operator = decision.Operator,
            Provider = decision.Provider,
            ProviderBilling = arrangement.Responsibility,
            ChargeUsage = arrangement.ChargesUsage,
            ChannelIntegrationId = arrangement.ChannelIntegrationId,
            MessageId = message.MessageId,
            ConversationId = message.ConversationId,
            CampaignId = message.CampaignId,
            Source = BillingSource.Channel
        };
        var hold = new MessageBillingHold(message.ClinicId, key, decision.SettleWhen);

        if (!arrangement.ChargesUsage)
        {
            // Analytics only: no money is held, so a recording failure must never block the message.
            try
            {
                await _billing.ReserveAsync(billableEvent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing: couldn't record uncharged usage {IdempotencyKey} for clinic {ClinicId}; the message is sent anyway.",
                    key, message.ClinicId);
            }
            return hold;
        }

        var result = await _billing.ReserveAsync(billableEvent, ct);
        if (!result.Succeeded)
        {
            throw BillingDeniedException.For(result.FailureReason ?? UsageFailureReason.RateNotFound);
        }
        return hold;
    }

    public async Task SendFailedAsync(MessageBillingHold? hold, CancellationToken ct = default)
    {
        if (hold is null) return;
        try
        {
            await _billing.ReleaseAsync(hold.ClinicId, hold.IdempotencyKey, "send_failed", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The send already failed and that error is what the caller reports. The hold isn't lost: the worker
            // resolves reservations past the timeout (and releases this one, since no message row exists).
            _logger.LogError(ex, "Billing: couldn't release usage {IdempotencyKey} for clinic {ClinicId} after a failed send; the maintenance worker will resolve it.",
                hold.IdempotencyKey, hold.ClinicId);
        }
    }

    public async Task SentAsync(MessageBillingHold? hold, CancellationToken ct = default)
    {
        if (hold is null || hold.SettleWhen != BillingSettlementTrigger.OnProviderAccepted) return;
        try
        {
            await _billing.SettleAsync(hold.ClinicId, hold.IdempotencyKey, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Billing: couldn't settle usage {IdempotencyKey} for clinic {ClinicId} after the send; the maintenance worker will resolve it.",
                hold.IdempotencyKey, hold.ClinicId);
        }
    }

    public async Task DeliveryStatusChangedAsync(Message message, string? status, CancellationToken ct = default)
    {
        if (!_config.BillingEnabled || string.IsNullOrWhiteSpace(status) || message.Direction != MessageDirection.Outbound) return;
        var policy = PolicyFor(message.Channel);
        if (policy is null) return;

        var action = policy.OnDeliveryStatus(status.Trim().ToLowerInvariant());
        if (action == DeliveryBillingAction.None) return;

        var key = MessageBillingKeys.For(message.Channel, message.Id);
        try
        {
            var result = action == DeliveryBillingAction.Settle
                ? await _billing.SettleAsync(message.ClinicId, key, null, ct)
                : await _billing.ReleaseAsync(message.ClinicId, key, "delivery_" + status.Trim().ToLowerInvariant(), ct);
            if (result.Outcome == UsageOutcome.NotFound)
            {
                _logger.LogDebug("Billing: no reservation for message {MessageId} (not billable when sent).", message.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Status callbacks are acknowledged to the provider regardless, so throwing wouldn't get a retry. The
            // reservation stays open and the maintenance worker settles/releases it from the message's state.
            _logger.LogError(ex, "Billing: couldn't apply status {Status} to usage {IdempotencyKey} for clinic {ClinicId}; the maintenance worker will resolve it.",
                status, key, message.ClinicId);
        }
    }

    public async Task<int> ResolveStaleReservationsAsync(CancellationToken ct = default)
    {
        if (!_config.BillingEnabled) return 0;
        var cutoff = _time.GetUtcNow().AddHours(-_config.BillingReservationTimeoutHours);

        List<BillingUsageRecord> stale;
        Dictionary<Guid, Message> messages;
        await using (var unit = _units.Create())
        {
            // Open SculptFlow holds, and uncharged usage whose provider outcome never arrived (analytics only).
            stale = (await unit.Usage.ListStaleReadOnlyAsync(cutoff, 500, ct)).ToList();
            var messageIds = stale.Where(u => u.MessageId.HasValue).Select(u => u.MessageId!.Value).ToList();
            messages = (await unit.Messages.ListByIdsReadOnlyAsync(messageIds, ct)).ToDictionary(m => m.Id);
        }

        var resolved = 0;
        foreach (var usage in stale)
        {
            Message? message = null;
            if (usage.MessageId is Guid id && messages.TryGetValue(id, out var m) && m.ClinicId == usage.ClinicId) message = m;

            var action = PolicyFor(usage.Channel)?.ResolveStale(message) ?? DeliveryBillingAction.Release;
            try
            {
                var result = action == DeliveryBillingAction.Settle
                    ? await _billing.SettleAsync(usage.ClinicId, usage.IdempotencyKey, null, ct)
                    : await _billing.ReleaseAsync(usage.ClinicId, usage.IdempotencyKey, "reservation_timeout", ct);
                if (!result.Duplicate && result.Outcome is UsageOutcome.Settled or UsageOutcome.Released or UsageOutcome.Recorded) resolved++;
                if (usage.ChargeStatus == ChargeStatus.Reserved)
                {
                    _logger.LogWarning("Billing: usage {IdempotencyKey} for clinic {ClinicId} had no outcome after {Hours}h; {Action}.",
                        usage.IdempotencyKey, usage.ClinicId, _config.BillingReservationTimeoutHours, action == DeliveryBillingAction.Settle ? "settled (message was delivered)" : "released");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Billing: couldn't resolve stale usage {IdempotencyKey} for clinic {ClinicId}; retrying next run.",
                    usage.IdempotencyKey, usage.ClinicId);
            }
        }
        return resolved;
    }

    private IChannelBillingPolicy? PolicyFor(string channel) => _policies.FirstOrDefault(p => p.Channel == channel);
}
