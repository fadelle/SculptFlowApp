using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Persistence.Contracts.Billing;

namespace Omni.App.Business.Services.Billing;

/// <summary>
/// The one billing line every dashboard page shows when something needs the client's attention (QA t2-billing-12: only
/// Settings → Billing said the trial had ended, and the dashboard said "all clear"). In order of importance: no plan
/// (none, expired or cancelled), a renewal that couldn't be paid (past due, with the grace period's end), a trial ending
/// within Billing:TrialWarningDays, and a balance below Billing:LowBalanceThreshold. Nothing while billing is off.
/// Cached per client for a minute (every page load asks); plan changes clear it with the entitlements.
/// </summary>
public class BillingBannerService : IBillingBannerService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private const string EndedKey = "billing.banner.ended";
    private const string TrialEndedKey = "billing.banner.trialEnded";
    private const string PastDueKey = "billing.banner.pastDue";
    private const string TrialEndsKey = "billing.banner.trialEnds";
    private const string LowBalanceKey = "billing.banner.lowBalance";

    private readonly IClientSubscriptionRepository _subscriptions;
    private readonly IBillingAccountRepository _accounts;
    private readonly ICacheManager _cache;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;

    public BillingBannerService(IClientSubscriptionRepository subscriptions, IBillingAccountRepository accounts, ICacheManager cache,
        IConfigManager config, TimeProvider time)
    {
        _subscriptions = subscriptions;
        _accounts = accounts;
        _cache = cache;
        _config = config;
        _time = time;
    }

    public async Task<BillingBanner?> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        if (!_config.BillingEnabled) return null;
        var banner = await _cache.GetOrCreateAsync(CacheKeys.BillingBanner(clientId), Ttl, c => BuildAsync(clientId, c), ct);
        return banner is { Kind: BillingBanner.None } ? null : banner;
    }

    private async Task<BillingBanner?> BuildAsync(Guid clientId, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var subscription = await _subscriptions.GetWithPlanReadOnlyAsync(clientId, ct);
        if (subscription?.Plan is null || subscription.Status is SubscriptionStatus.Expired or SubscriptionStatus.Cancelled)
        {
            var trialEnded = subscription?.Plan?.TrialDays is not null;
            return new BillingBanner(BillingBanner.Ended, trialEnded ? TrialEndedKey : EndedKey, null, null, null);
        }
        if (subscription.Status == SubscriptionStatus.PastDue)
        {
            var graceEnd = (subscription.PastDueSince ?? now).AddDays(_config.BillingGracePeriodDays);
            return new BillingBanner(BillingBanner.PastDue, PastDueKey, graceEnd, null, null);
        }
        if (subscription.Plan.TrialDays is not null && subscription.CurrentPeriodEnd - now <= TimeSpan.FromDays(_config.BillingTrialWarningDays))
        {
            return new BillingBanner(BillingBanner.TrialEnding, TrialEndsKey, subscription.CurrentPeriodEnd, null, null);
        }
        var threshold = _config.BillingLowBalanceThreshold;
        if (threshold > 0 && await _accounts.GetReadOnlyAsync(clientId, ct) is { } account && account.Spendable < threshold)
        {
            return new BillingBanner(BillingBanner.LowBalance, LowBalanceKey, null, account.Spendable, account.Currency);
        }
        return new BillingBanner(BillingBanner.None, "", null, null, null); // cached too: most pages have nothing to say
    }
}
