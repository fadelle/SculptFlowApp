using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Billing;

public enum OutboundMessageKind { Text, Template }

/// <summary>What a channel's billing policy may look at for an outbound message. No body, no provider payload.</summary>
/// <param name="MessageId">Generated before the send and reused for the Message row, so the reservation and the
/// later delivery callback share one idempotency key.</param>
/// <param name="RecipientAddress">Phone number (or other address) — used only to derive the destination country,
/// never stored.</param>
public sealed record OutboundMessageBillingContext(
    Guid ClinicId,
    string Channel,
    Guid MessageId,
    Guid ConversationId,
    Guid? CampaignId,
    OutboundMessageKind Kind,
    string? TemplateCategory,
    bool ServiceWindowOpen,
    string? RecipientAddress);

/// <summary>When reserved usage of a channel becomes billable.</summary>
public enum BillingSettlementTrigger
{
    /// <summary>When the provider confirms the outcome later (e.g. WhatsApp "delivered").</summary>
    OnDeliveryStatus,
    /// <summary>As soon as the provider accepts the send (e.g. a channel billed per submission).</summary>
    OnProviderAccepted
}

/// <summary>The channel's verdict for one outbound message: it is billable as this event.</summary>
public sealed record ChannelBillingDecision(
    string EventType,
    decimal Quantity,
    string Unit,
    string? CountryCode,
    string? Operator,
    string? Provider,
    BillingSettlementTrigger SettleWhen);

public enum DeliveryBillingAction { None, Settle, Release }

/// <summary>
/// A channel's billing rules — the ONLY place that knows how a channel/provider charges. Billing itself stays
/// generic. One implementation per channel (Integrations/WhatsApp/WhatsAppBillingPolicy.cs,
/// Integrations/Telegram/TelegramBillingPolicy.cs); a new channel adds one and registers it as IChannelBillingPolicy.
/// A channel with no policy is treated as free (and logged).
/// </summary>
public interface IChannelBillingPolicy
{
    /// <summary>The ConversationChannel value this policy covers.</summary>
    string Channel { get; }

    /// <summary>Before a send: is it billable, as what, how many units? null = not billable (no reservation).</summary>
    ChannelBillingDecision? DescribeOutbound(OutboundMessageBillingContext message);

    /// <summary>A delivery status arrived for a message that was reserved: settle, release, or wait.</summary>
    DeliveryBillingAction OnDeliveryStatus(string status);

    /// <summary>The outcome never arrived before the reservation timeout: decide from what the message row shows
    /// now (null when the message row doesn't exist, e.g. the app stopped right after the send).</summary>
    DeliveryBillingAction ResolveStale(Message? message);
}

/// <summary>A reservation made for an outbound message, carried from before the send to after it.</summary>
public sealed record MessageBillingHold(Guid ClinicId, string IdempotencyKey, BillingSettlementTrigger SettleWhen);

/// <summary>
/// The bridge between messaging and billing, used by MessageService. The channel policy says WHAT the usage is;
/// IProviderBillingService says, for the clinic's connected account, WHO pays the provider and WHETHER SculptFlow
/// charges. Only charged usage reserves money; everything else is just recorded.
///   ReserveOutboundAsync  before the provider call (throws BillingDeniedException when charged usage can't be paid)
///   SendFailedAsync       the provider call threw -> release
///   SentAsync             the provider accepted -> settle now if the channel bills on acceptance
///   DeliveryStatusChangedAsync  a status callback (any provider) -> the channel policy says settle / release
///   ResolveStaleReservationsAsync  worker: reservations whose outcome never arrived
/// Does nothing while Billing:Enabled is false.
/// </summary>
public interface IMessageBillingService
{
    Task<MessageBillingHold?> ReserveOutboundAsync(OutboundMessageBillingContext message, CancellationToken ct = default);
    Task SendFailedAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task SentAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task DeliveryStatusChangedAsync(Message message, string? status, CancellationToken ct = default);
    Task<int> ResolveStaleReservationsAsync(CancellationToken ct = default);
}

public static class MessageBillingKeys
{
    /// <summary>The idempotency key of an outbound message's usage: one message = one billable event.</summary>
    public static string For(string channel, Guid messageId) => $"{channel}:message:{messageId}";
}

public class MessageBillingService : IMessageBillingService
{
    private readonly IBillingService _billing;
    private readonly IEnumerable<IChannelBillingPolicy> _policies;
    private readonly IProviderBillingService _providerBilling;
    private readonly BillingDbFactory _dbFactory;
    private readonly BillingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MessageBillingService> _logger;

    public MessageBillingService(IBillingService billing, IEnumerable<IChannelBillingPolicy> policies, IProviderBillingService providerBilling,
        BillingDbFactory dbFactory, IOptions<BillingOptions> options, TimeProvider time, ILogger<MessageBillingService> logger)
    {
        _billing = billing;
        _policies = policies;
        _providerBilling = providerBilling;
        _dbFactory = dbFactory;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<MessageBillingHold?> ReserveOutboundAsync(OutboundMessageBillingContext message, CancellationToken ct = default)
    {
        if (!_options.Enabled) return null;

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
        if (!_options.Enabled || string.IsNullOrWhiteSpace(status) || message.Direction != MessageDirection.Outbound) return;
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
        if (!_options.Enabled) return 0;
        var cutoff = _time.GetUtcNow().AddHours(-Math.Max(1, _options.ReservationTimeoutHours));

        List<BillingUsageRecord> stale;
        Dictionary<Guid, Message> messages;
        await using (var db = _dbFactory.Create())
        {
            // Open SculptFlow holds, and uncharged usage whose provider outcome never arrived (analytics only).
            stale = await db.BillingUsageRecords.AsNoTracking()
                .Where(u => u.CreatedAt < cutoff
                            && (u.ChargeStatus == ChargeStatus.Reserved
                                || (u.ChargeStatus == ChargeStatus.NotCharged && u.ProviderOutcome == ProviderOutcome.Pending)))
                .OrderBy(u => u.CreatedAt)
                .Take(500)
                .ToListAsync(ct);
            var messageIds = stale.Where(u => u.MessageId.HasValue).Select(u => u.MessageId!.Value).ToList();
            messages = await db.Messages.AsNoTracking().Where(m => messageIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
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
                        usage.IdempotencyKey, usage.ClinicId, _options.ReservationTimeoutHours, action == DeliveryBillingAction.Settle ? "settled (message was delivered)" : "released");
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
