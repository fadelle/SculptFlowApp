using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Services.Billing;
using PlasticSurgery.Business.Services.Caching;
using PlasticSurgery.Business.Services.Clinics;
using PlasticSurgery.Business.Services.Configuration;
using PlasticSurgery.Business.Services.Dashboard;
using PlasticSurgery.Business.Services.Staff;

namespace PlasticSurgery.Tests.Services;

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
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
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

    private EntitlementService Sut() => new(_subscriptions.Object, _integrations.Object, _time, NullLogger<EntitlementService>.Instance, _config.Object, _cache.Object);

    private void Subscribe(string status, DateTimeOffset? pastDueSince = null, params (string Key, string Value)[] entitlements)
    {
        var plan = new SubscriptionPlan { Code = "pro", Name = "Pro" };
        foreach (var (k, v) in entitlements) plan.Entitlements.Add(new SubscriptionPlanEntitlement { EntitlementKey = k, Value = v });
        _subscriptions.Setup(s => s.GetWithEntitlementsReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicSubscription { ClinicId = _clinicId, Status = status, PastDueSince = pastDueSince, Plan = plan, CurrentPeriodEnd = _time.Now.AddDays(10) });
    }

    [Fact]
    public async Task Billing_off_means_unrestricted_and_nothing_is_loaded()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        var e = await Sut().GetAsync(_clinicId);
        Assert.False(e.BillingEnabled);
        Assert.True(e.CanSendMessages);
        await Sut().EnsureFeatureAsync(_clinicId, EntitlementKeys.Campaigns);
        await Sut().EnsureCanSendMessagesAsync(_clinicId);
        _subscriptions.Verify(s => s.GetWithEntitlementsReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Clinic_without_a_subscription_has_no_access()
    {
        var e = await Sut().GetAsync(_clinicId);
        Assert.False(e.HasAccess);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanSendMessagesAsync(_clinicId));
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureFeatureAsync(_clinicId, EntitlementKeys.Campaigns));
        Assert.Contains("subscription isn't active", ex.Message);
    }

    [Fact]
    public async Task Active_plan_grants_listed_features_only()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.Campaigns, "true"), (EntitlementKeys.AiAgent, "false"));
        var sut = Sut();
        await sut.EnsureFeatureAsync(_clinicId, EntitlementKeys.Campaigns);
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureFeatureAsync(_clinicId, EntitlementKeys.AiAgent));
        Assert.Contains("isn't included in your plan", ex.Message);
        var e = await sut.GetAsync(_clinicId);
        Assert.Equal(("pro", "Pro"), (e.PlanCode, e.PlanName));
        Assert.True(e.CanUseCampaigns);
    }

    [Fact]
    public async Task Past_due_keeps_access_only_during_the_grace_period()
    {
        Subscribe(SubscriptionStatus.PastDue, _time.Now.AddDays(-2));
        Assert.True((await Sut().GetAsync(_clinicId)).HasAccess);
        Subscribe(SubscriptionStatus.PastDue, _time.Now.AddDays(-4));
        Assert.False((await Sut().GetAsync(_clinicId)).HasAccess);
        Subscribe(SubscriptionStatus.PastDue, null);
        Assert.True((await Sut().GetAsync(_clinicId)).HasAccess); // unknown start counts as "just now"
        Subscribe(SubscriptionStatus.Expired);
        Assert.False((await Sut().GetAsync(_clinicId)).HasAccess);
        Subscribe(SubscriptionStatus.Cancelled);
        Assert.False((await Sut().GetAsync(_clinicId)).HasAccess);
    }

    [Fact]
    public async Task Limits_allow_up_to_the_cap_and_unlimited_is_open()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxAgents, "3"), (EntitlementKeys.MaxChannelConnections, EntitlementCatalog.Unlimited));
        var sut = Sut();
        await sut.EnsureWithinLimitAsync(_clinicId, EntitlementKeys.MaxAgents, 3);
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureWithinLimitAsync(_clinicId, EntitlementKeys.MaxAgents, 4));
        Assert.Contains("allows 3 staff seats", ex.Message);
        await sut.EnsureWithinLimitAsync(_clinicId, EntitlementKeys.MaxChannelConnections, 1000);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.EnsureWithinLimitAsync(_clinicId, EntitlementKeys.MaxWhatsAppNumbers, 1)); // missing key = 0
    }

    [Fact]
    public async Task Connecting_a_channel_counts_the_other_connected_channels()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxChannelConnections, "2"), (EntitlementKeys.MaxWhatsAppNumbers, "1"));
        _integrations.Setup(i => i.CountConnectedExceptAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        await Sut().EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp);
        _integrations.Setup(i => i.CountConnectedExceptAsync(_clinicId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp));
    }

    [Fact]
    public async Task Whatsapp_needs_a_whatsapp_number_allowance_but_other_channels_do_not()
    {
        Subscribe(SubscriptionStatus.Active, null, (EntitlementKeys.MaxChannelConnections, "5"), (EntitlementKeys.MaxWhatsAppNumbers, "0"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp));
        await Sut().EnsureCanConnectChannelAsync(_clinicId, ChannelType.Telegram);
    }

    [Fact]
    public async Task Connecting_is_unrestricted_when_billing_is_off()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        await Sut().EnsureCanConnectChannelAsync(_clinicId, ChannelType.WhatsApp);
        _integrations.Verify(i => i.CountConnectedExceptAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Entitlements_are_resolved_once_per_service_instance()
    {
        Subscribe(SubscriptionStatus.Active);
        var sut = Sut();
        await sut.GetAsync(_clinicId);
        await sut.GetAsync(_clinicId);
        _cache.Verify(c => c.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<Func<CancellationToken, Task<SubscriptionSnapshot?>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class StaffDashboardClinicProfileTests
{
    private readonly Guid _clinicId = Guid.NewGuid();

    [Fact]
    public async Task Staff_are_listed_active_first_then_by_name_or_email()
    {
        var clinics = new Mock<IClinicRepository>();
        clinics.Setup(c => c.ListStaffAsync(_clinicId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<StaffMemberRow>
        {
            new("1", "z@x.com", false, DateTimeOffset.UtcNow, "Alice"),
            new("2", "b@x.com", true, DateTimeOffset.UtcNow, null),
            new("3", "a@x.com", true, DateTimeOffset.UtcNow, "carol"),
            new("4", "d@x.com", true, DateTimeOffset.UtcNow, "Bob")
        });
        var list = await new StaffService(clinics.Object).ListAsync(_clinicId);
        Assert.Equal(["2", "4", "3", "1"], list.Select(s => s.UserId));
    }

    [Fact]
    public async Task Dashboard_summary_and_attention_map_repository_counts()
    {
        var repo = new Mock<IDashboardRepository>();
        repo.Setup(r => r.GetSummaryCountsAsync(_clinicId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSummaryCounts(10, 5, 4, 2, 1500m));
        repo.Setup(r => r.GetAttentionCountsAsync(_clinicId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardAttentionCounts(3, 2, 1));
        var sut = new DashboardService(repo.Object);
        var s = await sut.GetSummaryAsync(_clinicId);
        Assert.Equal((10, 5, 4, 2, 1500m, 0), (s.NewInterestedPeople, s.ConsultationsBooked, s.ConsultationsAttended, s.SurgeriesBooked, s.Revenue, s.OldLeadsRecovered));
        var a = await sut.GetAttentionAsync(_clinicId);
        Assert.Equal((3, 2, 1), (a.ConversationsNeedingStaff, a.OverdueFollowups, a.AppointmentsNeedingOutcome));
        await sut.GetLeadsAsync(_clinicId, "new", "q", "src", 0, 10);
        await sut.GetAppointmentsAsync(_clinicId, "booked", "q", 0, 10);
        await sut.GetProcedureStatsAsync(_clinicId);
        repo.Verify(r => r.ListLeadsAsync(_clinicId, "new", "q", "src", 0, 10, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.ListAppointmentsAsync(_clinicId, "booked", "q", 0, 10, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetProcedureStatsAsync(_clinicId, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clinic_profile_update_validates_name_and_copies_details()
    {
        var clinics = new Mock<IClinicRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clinic = new Clinic { Id = _clinicId, Name = "Old" };
        clinics.Setup(c => c.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        var sut = new ClinicProfileService(clinics.Object, uow.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clinicId, new UpdateClinicDetailsRequest(" ", null, null, null, null, null, null)));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clinicId, new UpdateClinicDetailsRequest(new string('n', 201), null, null, null, null, null, null)));
        await sut.UpdateDetailsAsync(_clinicId, new UpdateClinicDetailsRequest(" New Name ", "123", "a@b.c", "https://x", "street", "9-5", "info"));
        Assert.Equal(("New Name", "123", "a@b.c", "https://x", "street", "9-5", "info"), (clinic.Name, clinic.Phone, clinic.Email, clinic.Website, clinic.Address, clinic.OperatingHours, clinic.ConsultationInfo));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        clinics.Setup(c => c.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);
        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateDetailsAsync(_clinicId, new UpdateClinicDetailsRequest("x", null, null, null, null, null, null)));
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
