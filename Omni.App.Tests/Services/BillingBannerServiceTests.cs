using Omni.App.Business.Providers.Caching;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Services.Billing;
using Omni.App.Entities.Dtos.Billing;

namespace Omni.App.Tests.Services;

/// <summary>QA t2-billing-12: every dashboard page says when the plan needs the client's attention.</summary>
public class BillingBannerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IClientSubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly ManualTimeProvider _time = new(Now);

    public BillingBannerServiceTests()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(true);
        _config.SetupGet(c => c.BillingGracePeriodDays).Returns(7);
        _config.SetupGet(c => c.BillingTrialWarningDays).Returns(3);
        _config.SetupGet(c => c.BillingLowBalanceThreshold).Returns(5m);
    }

    private BillingBannerService Sut() => new(_subscriptions.Object, _accounts.Object,
        new CacheManager(new MemoryCacheAdapter(_time), NullLogger<CacheManager>.Instance), _config.Object, _time);

    private void Subscription(string status, int? trialDays = null, DateTimeOffset? periodEnd = null, DateTimeOffset? pastDueSince = null) =>
        _subscriptions.Setup(s => s.GetWithPlanReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new ClientSubscription
        {
            ClientId = _clientId, Status = status, PastDueSince = pastDueSince, CurrentPeriodStart = Now.AddDays(-10),
            CurrentPeriodEnd = periodEnd ?? Now.AddDays(20), Plan = new SubscriptionPlan { Code = "p", Name = "Pro", TrialDays = trialDays }
        });

    private void Balance(decimal wallet, decimal credit = 0m) =>
        _accounts.Setup(a => a.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { ClientId = _clientId, WalletBalance = wallet, IncludedCreditBalance = credit, Currency = "USD" });

    [Fact]
    public async Task No_plan_or_an_ended_one_is_urgent()
    {
        var none = (await Sut().GetAsync(_clientId))!;
        Assert.Equal(("billing.banner.ended", true), (none.TextKey, none.Urgent));
        Subscription(SubscriptionStatus.Cancelled);
        Assert.Equal(BillingBanner.Ended, (await Sut().GetAsync(_clientId))!.Kind);
    }

    [Fact]
    public async Task An_ended_trial_says_so()
    {
        Subscription(SubscriptionStatus.Expired, trialDays: 7);
        var banner = (await Sut().GetAsync(_clientId))!;
        Assert.Equal((BillingBanner.Ended, "billing.banner.trialEnded", true), (banner.Kind, banner.TextKey, banner.Urgent));
    }

    [Fact]
    public async Task An_unpaid_renewal_shows_when_the_grace_period_ends()
    {
        Subscription(SubscriptionStatus.PastDue, pastDueSince: Now.AddDays(-2));
        var banner = (await Sut().GetAsync(_clientId))!;
        Assert.Equal((BillingBanner.PastDue, Now.AddDays(5)), (banner.Kind, banner.When));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task A_trial_warns_in_its_last_days(int daysLeft, bool warns)
    {
        Subscription(SubscriptionStatus.Active, trialDays: 7, periodEnd: Now.AddDays(daysLeft));
        Balance(100m);
        var banner = await Sut().GetAsync(_clientId);
        Assert.Equal(warns, banner?.Kind == BillingBanner.TrialEnding);
    }

    [Theory]
    [InlineData(4.99, 0, true)]
    [InlineData(3, 2, false)]
    [InlineData(50, 0, false)]
    public async Task A_low_balance_warns_below_the_threshold(decimal wallet, decimal credit, bool warns)
    {
        Subscription(SubscriptionStatus.Active);
        Balance(wallet, credit);
        var banner = await Sut().GetAsync(_clientId);
        Assert.Equal(warns, banner?.Kind == BillingBanner.LowBalance);
        if (warns) Assert.Equal((wallet + credit, "USD", false), (banner!.Amount, banner.Currency, banner.Urgent));
    }

    [Fact]
    public async Task Nothing_while_billing_is_off_or_all_is_well()
    {
        Subscription(SubscriptionStatus.Active);
        Balance(100m);
        Assert.Null(await Sut().GetAsync(_clientId));
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        _subscriptions.Reset();
        Assert.Null(await Sut().GetAsync(_clientId));
    }
}
