using System.Globalization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Persistence.Contracts.Billing;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Business.Services.Billing;

public class EntitlementService : IEntitlementService
{
    private readonly IClientSubscriptionRepository _subscriptions;
    private readonly IChannelIntegrationRepository _integrations;
    private readonly TimeProvider _time;
    private readonly ILogger<EntitlementService> _logger;
    private readonly IConfigManager _config;
    private readonly ICacheManager _cacheManager;

    public EntitlementService(IClientSubscriptionRepository subscriptions, IChannelIntegrationRepository integrations, TimeProvider time, ILogger<EntitlementService> logger, IConfigManager config,
        ICacheManager cacheManager)
    {
        _config = config;
        _cacheManager = cacheManager;
        _subscriptions = subscriptions;
        _integrations = integrations;
        _time = time;
        _logger = logger;
    }

    public async Task<ClientEntitlements> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        if (!_config.BillingEnabled) return ClientEntitlements.Unrestricted;
        var snapshot = (await _cacheManager.GetOrCreateAsync(CacheKeys.Entitlements(clientId), CacheKeys.EntitlementsTtl,
            async token => await LoadSnapshotAsync(clientId, token), ct))!;

        ClientEntitlements result;
        if (snapshot.PlanCode is null)
        {
            result = new ClientEntitlements(true, false, null, null, null, null, new Dictionary<string, string>());
        }
        else
        {
            var now = _time.GetUtcNow();
            var hasAccess = snapshot.Status == SubscriptionStatus.Active
                || (snapshot.Status == SubscriptionStatus.PastDue
                    && now < (snapshot.PastDueSince ?? now).AddDays(_config.BillingGracePeriodDays));
            result = new ClientEntitlements(true, hasAccess, snapshot.Status, snapshot.PlanCode, snapshot.PlanName,
                snapshot.CurrentPeriodEnd, snapshot.Entitlements);
        }
        return result;
    }

    public async Task EnsureCanSendMessagesAsync(Guid clientId, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clientId, ct);
        if (entitlements.CanSendMessages) return;
        _logger.LogInformation("Billing: client {ClientId} blocked from sending — subscription {Status}.", clientId, entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException("subscription",
            "Your subscription isn't active, so messages can't be sent. See Settings → Billing.");
    }

    public async Task EnsureFeatureAsync(Guid clientId, string featureKey, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clientId, ct);
        if (entitlements.IsEnabled(featureKey)) return;
        var label = EntitlementCatalog.Find(featureKey)?.Label ?? featureKey;
        _logger.LogInformation("Billing: client {ClientId} blocked from {Feature} (plan {Plan}, status {Status}).",
            clientId, featureKey, entitlements.PlanCode ?? "none", entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException(featureKey, entitlements.HasAccess
            ? $"{label} isn't included in your plan. See Settings → Billing."
            : $"Your subscription isn't active, so {label.ToLowerInvariant()} can't be used. See Settings → Billing.");
    }

    public async Task EnsureWithinLimitAsync(Guid clientId, string limitKey, int countAfterChange, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clientId, ct);
        var limit = entitlements.Limit(limitKey);
        if (limit is null || countAfterChange <= limit) return;
        var label = EntitlementCatalog.Find(limitKey)?.Label ?? limitKey;
        _logger.LogInformation("Billing: client {ClientId} reached its {Limit} limit ({Max}).", clientId, limitKey, limit);
        throw new EntitlementDeniedException(limitKey, entitlements.HasAccess
            ? $"Your plan allows {limit} {label.ToLowerInvariant()}. See Settings → Billing."
            : "Your subscription isn't active. See Settings → Billing.");
    }

    public async Task EnsureCanConnectChannelAsync(Guid clientId, string channel, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clientId, ct);
        if (!entitlements.BillingEnabled) return;

        // One row per (client, channel): reconnecting a channel replaces its row, so only the OTHER channels count.
        var otherConnected = await _integrations.CountConnectedExceptAsync(clientId, channel, ct);
        await EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxChannelConnections, otherConnected + 1, ct);
        if (channel == ChannelType.WhatsApp)
        {
            await EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxWhatsAppNumbers, 1, ct);
        }
    }

    private async Task<SubscriptionSnapshot> LoadSnapshotAsync(Guid clientId, CancellationToken ct)
    {
        var subscription = await _subscriptions.GetWithEntitlementsReadOnlyAsync(clientId, ct);
        if (subscription?.Plan is null) return new SubscriptionSnapshot(null, null, null, null, null, new Dictionary<string, string>());
        return new SubscriptionSnapshot(subscription.Status, subscription.PastDueSince, subscription.Plan.Code, subscription.Plan.Name,
            subscription.CurrentPeriodEnd, subscription.Plan.Entitlements.ToDictionary(x => x.EntitlementKey, x => x.Value));
    }
}
