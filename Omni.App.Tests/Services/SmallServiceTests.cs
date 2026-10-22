using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Services.Billing;
using Omni.App.Business.Services.Caching;
using Omni.App.Business.Services.Clients;
using Omni.App.Business.Services.Configuration;
using Omni.App.Business.Services.Dashboard;

namespace Omni.App.Tests.Services;

public class SettingsServiceTests
{
    private readonly Mock<ISettingRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly ConfigDefinition _int = ConfigDefaults.All.First(d => d.Type == ConfigValueType.Int && d.Min is not null && d.Max is not null);
    private readonly ConfigDefinition _secret = ConfigDefaults.All.First(d => d.IsSecret);

    private SettingsService Sut() => new(_repo.Object, _uow.Object, _config.Object);

    public SettingsServiceTests()
    {
        _config.Setup(c => c.GetString(It.IsAny<ConfigDefinition>())).Returns((ConfigDefinition d) => d.DefaultValue);
        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ConfigSetting>());
    }

    [Fact]
    public async Task List_returns_every_declared_setting_in_order_with_stored_overrides()
    {
        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ConfigSetting>
        {
            new() { Section = _int.Section.ToLowerInvariant(), Key = _int.Key.ToLowerInvariant(), Value = "7", UpdatedBy = "admin" }
        });
        var list = await Sut().ListAsync();
        Assert.Equal(ConfigDefaults.All.Count, list.Count);
        Assert.Equal(list.OrderBy(l => l.Section).ThenBy(l => l.Key).Select(l => l.Key), list.Select(l => l.Key));
        var row = list.Single(l => l.Section == _int.Section && l.Key == _int.Key);
        Assert.Equal(("7", "admin"), (row.StoredValue, row.UpdatedBy));
    }

    [Fact]
    public async Task Secrets_are_never_returned_only_masked()
    {
        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ConfigSetting> { new() { Section = _secret.Section, Key = _secret.Key, Value = "sk-super-secret" } });
        _config.Setup(c => c.GetString(_secret)).Returns("sk-super-secret");
        var row = (await Sut().ListAsync()).Single(l => l.Section == _secret.Section && l.Key == _secret.Key);
        Assert.Equal(SettingsService.SecretMask, row.StoredValue);
        Assert.Equal(SettingsService.SecretMask, row.EffectiveValue);
        Assert.Equal("", row.DefaultValue);
        Assert.True(row.IsSecret);

        _config.Setup(c => c.GetString(_secret)).Returns("");
        Assert.Equal("", (await Sut().ListAsync()).Single(l => l.Section == _secret.Section && l.Key == _secret.Key).EffectiveValue);
    }

    [Fact]
    public async Task Set_creates_a_row_trims_refreshes_the_snapshot_and_records_the_actor()
    {
        ConfigSetting? added = null;
        _repo.Setup(r => r.Add(It.IsAny<ConfigSetting>())).Callback<ConfigSetting>(s => added = s);
        var mid = ((int)(_int.Min!.Value + _int.Max!.Value) / 2).ToString();
        var r = await Sut().SetAsync(_int.Section, _int.Key, $" {mid} ", " because ", "admin@x.com");
        Assert.Equal((mid, "because", "admin@x.com"), (added!.Value, added.Description, added.UpdatedBy));
        _config.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(_int.Key, r.Key);
    }

    [Fact]
    public async Task Set_updates_an_existing_row_without_adding()
    {
        var row = new ConfigSetting { Section = _int.Section, Key = _int.Key, Value = "old" };
        _repo.Setup(r => r.GetForUpdateAsync(_int.Section, _int.Key, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var v = ((int)_int.Min!.Value).ToString();
        await Sut().SetAsync(_int.Section.ToUpperInvariant(), _int.Key, v, null, "a");
        Assert.Equal((v, null), (row.Value, row.Description));
        _repo.Verify(r => r.Add(It.IsAny<ConfigSetting>()), Times.Never);
    }

    [Theory]
    [InlineData("Nope", "Nope", "1")]
    [InlineData(null, null, "1")]
    public async Task Set_rejects_undeclared_settings(string? section, string? key, string value) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(section!, key!, value, null, "a"));

    [Fact]
    public async Task Set_validates_value_and_note()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, "  ", null, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, null, null, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, new string('1', ConfigSettingLimits.MaxValueLength + 1), null, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, "not-a-number", null, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, ((int)_int.Max!.Value + 1).ToString(), null, "a"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetAsync(_int.Section, _int.Key, ((int)_int.Min!.Value).ToString(), new string('n', ConfigSettingLimits.MaxDescriptionLength + 1), "a"));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reset_removes_the_override_and_reloads()
    {
        Assert.False(await Sut().ResetAsync(_int.Section, _int.Key));
        var row = new ConfigSetting { Section = _int.Section, Key = _int.Key, Value = "1" };
        _repo.Setup(r => r.GetForUpdateAsync(_int.Section, _int.Key, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        Assert.True(await Sut().ResetAsync(_int.Section, _int.Key));
        _repo.Verify(r => r.Remove(row), Times.Once);
        _config.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ResetAsync("x", "y"));
    }
}

public class EntitlementServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IClientSubscriptionRepository> _subscriptions = new();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<ICacheManager> _cache = new();
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public EntitlementServiceTests()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(true);
        _config.SetupGet(c => c.BillingGracePeriodDays).Returns(3);
        _cache.Setup(c => c.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<Func<CancellationToken, Task<SubscriptionSnapshot?>>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, TimeSpan _, Func<CancellationToken, Task<SubscriptionSnapshot?>> f, CancellationToken ct) => f(ct));
    }

    private static IBillingAddonRepository NoAddons()
    {
        var addons = new Mock<IBillingAddonRepository>();
        addons.Setup(a => a.ListForClientReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientAddon>());
        return addons.Object;
    }

    private EntitlementService Sut() => new(_subscriptions.Object, NoAddons(), _integrations.Object, _time, NullLogger<EntitlementService>.Instance, _config.Object, _cache.Object);

    private void Subscribe(string status, DateTimeOffset? pastDueSince = null, params (string Key, string Value)[] entitlements)
    {
        var plan = new SubscriptionPlan { Code = "pro", Name = "Pro" };
        foreach (var (k, v) in entitlements) plan.Entitlements.Add(new SubscriptionPlanEntitlement { EntitlementKey = k, Value = v });
        _subscriptions.Setup(s => s.GetWithEntitlementsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientSubscription { ClientId = _clientId, Status = status, PastDueSince = pastDueSince, Plan = plan, CurrentPeriodEnd = _time.Now.AddDays(10) });
    }

    [Fact]
    public async Task Billing_off_means_unrestricted_and_nothing_is_loaded()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        var e = await Sut().GetAsync(_clientId);
        Assert.False(e.BillingEnabled);
        Assert.True(e.CanSendMessages);
        await Sut().EnsureFeatureAsync(_clientId, EntitlementKeys.Campaigns);
        await Sut().EnsureCanSendMessagesAsync(_clientId);
        _subscriptions.Verify(s => s.GetWithEntitlementsReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Client_without_a_subscription_has_no_access()
    {
        var e = await Sut().GetAsync(_clientId);
        Assert.False(e.HasAccess);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanSendMessagesAsync(_clientId));
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureFeatureAsync(_clientId, EntitlementKeys.Campaigns));
        Assert.Contains("subscription isn't active", TextKit.English(ex));
    }

    [Fact]
    public async Task Active_plan_grants_listed_features_only()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.Campaigns, "true"), (EntitlementKeys.AiAssistant, "false"));
        var sut = Sut();
        await sut.EnsureFeatureAsync(_clientId, EntitlementKeys.Campaigns);
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureFeatureAsync(_clientId, EntitlementKeys.AiAssistant));
        Assert.Contains("isn't included in your plan", TextKit.English(ex));
        var e = await sut.GetAsync(_clientId);
        Assert.Equal(("pro", "Pro"), (e.PlanCode, e.PlanName));
        Assert.True(e.CanUseCampaigns);
    }

    [Fact]
    public async Task Past_due_keeps_access_only_during_the_grace_period()
    {
        Subscribe(SubscriptionStatus.PastDue, _time.Now.AddDays(-2));
        Assert.True((await Sut().GetAsync(_clientId)).HasAccess);
        Subscribe(SubscriptionStatus.PastDue, _time.Now.AddDays(-4));
        Assert.False((await Sut().GetAsync(_clientId)).HasAccess);
        Subscribe(SubscriptionStatus.PastDue, null);
        Assert.True((await Sut().GetAsync(_clientId)).HasAccess); // unknown start counts as "just now"
        Subscribe(SubscriptionStatus.Expired);
        Assert.False((await Sut().GetAsync(_clientId)).HasAccess);
        Subscribe(SubscriptionStatus.Cancelled);
        Assert.False((await Sut().GetAsync(_clientId)).HasAccess);
    }

    [Fact]
    public async Task Limits_allow_up_to_the_cap_and_unlimited_is_open()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxUsers, "3"), (EntitlementKeys.MaxChannelConnections, EntitlementCatalog.Unlimited));
        var sut = Sut();
        await sut.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3);
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 4));
        Assert.Contains("limit for Seats (3)", TextKit.English(ex));
        await sut.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxChannelConnections, 1000);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxWhatsAppNumbers, 1)); // missing key = 0
    }

    [Fact]
    public async Task Connecting_a_channel_counts_the_other_connected_channels()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxChannelConnections, "2"), (EntitlementKeys.MaxWhatsAppNumbers, "1"));
        _integrations.Setup(i => i.CountConnectedExceptAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        await Sut().EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp);
        _integrations.Setup(i => i.CountConnectedExceptAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp));
    }

    [Fact]
    public async Task Whatsapp_needs_a_whatsapp_number_allowance_but_other_channels_do_not()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxChannelConnections, "5"), (EntitlementKeys.MaxWhatsAppNumbers, "0"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp));
        await Sut().EnsureCanConnectChannelAsync(_clientId, ChannelType.Telegram);
    }

    [Fact]
    public async Task Connecting_is_unrestricted_when_billing_is_off()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        await Sut().EnsureCanConnectChannelAsync(_clientId, ChannelType.WhatsApp);
        _integrations.Verify(i => i.CountConnectedExceptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Entitlements_are_read_through_the_cache_manager_every_time()
    {
        Subscribe(SubscriptionStatus.Active);
        var sut = Sut();
        await sut.GetAsync(_clientId);
        await sut.GetAsync(_clientId);
        _cache.Verify(c => c.GetOrCreateAsync(CacheKeys.Entitlements(_clientId), It.IsAny<TimeSpan>(), It.IsAny<Func<CancellationToken, Task<SubscriptionSnapshot?>>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}

public class MemberDashboardClientProfileTests
{
    private readonly Guid _clientId = Guid.NewGuid();

    [Fact]
    public async Task Members_are_listed_active_first_then_by_name_or_email()
    {
        var clients = new Mock<IClientRepository>();
        clients.Setup(c => c.ListMembersAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientMemberRow>
        {
            new("1", "z@x.com", "Alice", UserRole.Member, false, DateTimeOffset.UtcNow, null),
            new("2", "b@x.com", null, UserRole.Member, true, DateTimeOffset.UtcNow, null),
            new("3", "a@x.com", "carol", UserRole.Member, true, DateTimeOffset.UtcNow, null),
            new("4", "d@x.com", "Bob", UserRole.Client, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        var list = await MemberServiceTests.Sut(clients.Object).ListAsync(_clientId);
        Assert.Equal(["2", "4", "3", "1"], list.Select(s => s.UserId));
        Assert.Equal(UserRole.Client, list.Single(m => m.UserId == "4").Role);
    }

    [Fact]
    public async Task Dashboard_summary_and_attention_map_repository_counts()
    {
        var repo = new Mock<IDashboardRepository>();
        repo.Setup(r => r.GetSummaryCountsAsync(_clientId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSummaryCounts(10, 5, 4, 2, 1500m));
        repo.Setup(r => r.GetAttentionCountsAsync(_clientId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardAttentionCounts(3, 2, 1));
        var sut = new DashboardService(repo.Object);
        var s = await sut.GetSummaryAsync(_clientId);
        Assert.Equal((10, 5, 4, 2, 1500m, 0), (s.NewInterestedPeople, s.BookingsMade, s.BookingsAttended, s.OrdersMade, s.Revenue, s.OldContactsRecovered));
        var a = await sut.GetAttentionAsync(_clientId);
        Assert.Equal((3, 2, 1), (a.ConversationsNeedingHuman, a.OverdueFollowups, a.BookingsNeedingOutcome));
        await sut.GetContactsAsync(_clientId, "new", "q", "src", 0, 10);
        await sut.GetBookingsAsync(_clientId, "booked", "q", 0, 10);
        await sut.GetServiceStatsAsync(_clientId);
        repo.Verify(r => r.ListContactsAsync(_clientId, "new", "q", "src", 0, 10, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.ListBookingsAsync(_clientId, "booked", "q", 0, 10, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetServiceStatsAsync(_clientId, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_the_account_owner_or_the_platform_admin_sets_the_default_language()
    {
        var clients = new Mock<IClientRepository>();
        var uow = new Mock<IUnitOfWork>();
        var client = new Client { Id = _clientId, DefaultLanguage = "en" };
        clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var sut = new ClientProfileService(clients.Object, uow.Object);
        var owner = new Omni.App.Entities.Dtos.Clients.UserAccess("o", _clientId, UserRole.Client, null, null, null);
        var member = owner with { Role = UserRole.Member };

        Assert.Equal("clients.errors.defaultLanguageOwnerOnly",
            (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SetDefaultLanguageAsync(member, "ar"))).Message);
        await sut.SetDefaultLanguageAsync(owner, "AR");
        Assert.Equal("ar", client.DefaultLanguage);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetDefaultLanguageAsync(owner, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SetDefaultLanguageAsync(_clientId, "es"));
        await sut.SetDefaultLanguageAsync(_clientId, "fr");
        Assert.Equal("fr", client.DefaultLanguage);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

        clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync((Client?)null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.SetDefaultLanguageAsync(_clientId, "en"));
    }

    [Fact]
    public async Task Client_profile_update_validates_name_and_copies_details()
    {
        var clients = new Mock<IClientRepository>();
        var uow = new Mock<IUnitOfWork>();
        var client = new Client { Id = _clientId, Name = "Old" };
        clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var sut = new ClientProfileService(clients.Object, uow.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clientId, new UpdateClientDetailsRequest(" ", null, null, null, null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clientId, new UpdateClientDetailsRequest(new string('n', 201), null, null, null, null, null, null)));
        await sut.UpdateDetailsAsync(_clientId, new UpdateClientDetailsRequest(" New Name ", "123", "a@b.c", "https://x", "street", "9-5", "info"));
        Assert.Equal(("New Name", "123", "a@b.c", "https://x", "street", "9-5", "info"), (client.Name, client.Phone, client.Email, client.Website, client.Address, client.OperatingHours, client.BookingInfo));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync((Client?)null);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clientId, new UpdateClientDetailsRequest("x", null, null, null, null, null, null)));
    }
}

public class CacheAdminServiceTests
{
    private readonly Mock<ICacheManager> _cache = new();
    private readonly Mock<IConfigManager> _config = new();

    private CacheAdminService Sut() => new(_cache.Object, _config.Object, NullLogger<CacheAdminService>.Instance);

    [Fact]
    public async Task Get_returns_the_cached_json_or_404_semantics()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync("k", default));
        var info = new CacheEntryInfo("k", DateTimeOffset.UtcNow.AddMinutes(1), 12);
        _cache.Setup(c => c.GetRawAsync("k", It.IsAny<CancellationToken>())).ReturnsAsync((info, "{\"a\":1}"));
        var r = await Sut().GetAsync("k", default);
        Assert.Equal(("k", 12), (r.Key, r.SizeBytes));
        Assert.Equal(1, r.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task Remove_by_key_prefix_and_clear()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().RemoveAsync("missing", default));
        _cache.Setup(c => c.RemoveAsync("k", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().RemoveAsync("k", default);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().RemoveByPrefixAsync(" ", default));
        _cache.Setup(c => c.RemoveByPrefixAsync("billing:", It.IsAny<CancellationToken>())).ReturnsAsync(3);
        Assert.Equal(3, (await Sut().RemoveByPrefixAsync("billing:", default)).Removed);

        _cache.Setup(c => c.ClearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(9);
        Assert.Equal(9, (await Sut().ClearAsync(default)).Removed);
        _config.Verify(c => c.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_delegates()
    {
        _cache.Setup(c => c.ListAsync("p", It.IsAny<CancellationToken>())).ReturnsAsync(new List<CacheEntryInfo> { new("p:1", DateTimeOffset.UtcNow, 1) });
        Assert.Single(await Sut().ListAsync("p", default));
    }
}
