using System.Globalization;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Billing;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Persistence.Contracts.Billing;
using PlasticSurgery.Persistence.Contracts.Channels;

namespace PlasticSurgery.Business.Services.Billing;

public class EntitlementService : IEntitlementService
{
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IChannelIntegrationRepository _integrations;
    private readonly TimeProvider _time;
    private readonly ILogger<EntitlementService> _logger;
    private readonly IConfigManager _config;
    private readonly ICacheManager _cacheManager;
    private readonly Dictionary<Guid, ClinicEntitlements> _cache = new(); // per request (scoped service)

    public EntitlementService(IClinicSubscriptionRepository subscriptions, IChannelIntegrationRepository integrations, TimeProvider time, ILogger<EntitlementService> logger, IConfigManager config,
        ICacheManager cacheManager)
    {
        _config = config;
        _cacheManager = cacheManager;
        _subscriptions = subscriptions;
        _integrations = integrations;
        _time = time;
        _logger = logger;
    }

    public async Task<ClinicEntitlements> GetAsync(Guid clinicId, CancellationToken ct = default)
    {
        if (!_config.BillingEnabled) return ClinicEntitlements.Unrestricted;
        if (_cache.TryGetValue(clinicId, out var cached)) return cached;

        var snapshot = (await _cacheManager.GetOrCreateAsync(CacheKeys.Entitlements(clinicId), CacheKeys.EntitlementsTtl,
            async token => await LoadSnapshotAsync(clinicId, token), ct))!;

        ClinicEntitlements result;
        if (snapshot.PlanCode is null)
        {
            result = new ClinicEntitlements(true, false, null, null, null, null, new Dictionary<string, string>());
        }
        else
        {
            var now = _time.GetUtcNow();
            var hasAccess = snapshot.Status == SubscriptionStatus.Active
                || (snapshot.Status == SubscriptionStatus.PastDue
                    && now < (snapshot.PastDueSince ?? now).AddDays(_config.BillingGracePeriodDays));
            result = new ClinicEntitlements(true, hasAccess, snapshot.Status, snapshot.PlanCode, snapshot.PlanName,
                snapshot.CurrentPeriodEnd, snapshot.Entitlements);
        }
        _cache[clinicId] = result;
        return result;
    }

    public async Task EnsureCanSendMessagesAsync(Guid clinicId, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (entitlements.CanSendMessages) return;
        _logger.LogInformation("Billing: clinic {ClinicId} blocked from sending — subscription {Status}.", clinicId, entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException("subscription",
            "Your subscription isn't active, so messages can't be sent. See Settings → Billing.");
    }

    public async Task EnsureFeatureAsync(Guid clinicId, string featureKey, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (entitlements.IsEnabled(featureKey)) return;
        var label = EntitlementCatalog.Find(featureKey)?.Label ?? featureKey;
        _logger.LogInformation("Billing: clinic {ClinicId} blocked from {Feature} (plan {Plan}, status {Status}).",
            clinicId, featureKey, entitlements.PlanCode ?? "none", entitlements.SubscriptionStatus ?? "none");
        throw new EntitlementDeniedException(featureKey, entitlements.HasAccess
            ? $"{label} isn't included in your plan. See Settings → Billing."
            : $"Your subscription isn't active, so {label.ToLowerInvariant()} can't be used. See Settings → Billing.");
    }

    public async Task EnsureWithinLimitAsync(Guid clinicId, string limitKey, int countAfterChange, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        var limit = entitlements.Limit(limitKey);
        if (limit is null || countAfterChange <= limit) return;
        var label = EntitlementCatalog.Find(limitKey)?.Label ?? limitKey;
        _logger.LogInformation("Billing: clinic {ClinicId} reached its {Limit} limit ({Max}).", clinicId, limitKey, limit);
        throw new EntitlementDeniedException(limitKey, entitlements.HasAccess
            ? $"Your plan allows {limit} {label.ToLowerInvariant()}. See Settings → Billing."
            : "Your subscription isn't active. See Settings → Billing.");
    }

    public async Task EnsureCanConnectChannelAsync(Guid clinicId, string channel, CancellationToken ct = default)
    {
        var entitlements = await GetAsync(clinicId, ct);
        if (!entitlements.BillingEnabled) return;

        // One row per (clinic, channel): reconnecting a channel replaces its row, so only the OTHER channels count.
        var otherConnected = await _integrations.CountConnectedExceptAsync(clinicId, channel, ct);
        await EnsureWithinLimitAsync(clinicId, EntitlementKeys.MaxChannelConnections, otherConnected + 1, ct);
        if (channel == ChannelType.WhatsApp)
        {
            await EnsureWithinLimitAsync(clinicId, EntitlementKeys.MaxWhatsAppNumbers, 1, ct);
        }
    }

    private async Task<SubscriptionSnapshot> LoadSnapshotAsync(Guid clinicId, CancellationToken ct)
    {
        var subscription = await _subscriptions.GetWithEntitlementsReadOnlyAsync(clinicId, ct);
        if (subscription?.Plan is null) return new SubscriptionSnapshot(null, null, null, null, null, new Dictionary<string, string>());
        return new SubscriptionSnapshot(subscription.Status, subscription.PastDueSince, subscription.Plan.Code, subscription.Plan.Name,
            subscription.CurrentPeriodEnd, subscription.Plan.Entitlements.ToDictionary(x => x.EntitlementKey, x => x.Value));
    }
}
