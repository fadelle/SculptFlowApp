using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Services.Clinics;
using PlasticSurgery.Business.Services.TikTok;

namespace PlasticSurgery.Tests.Services;

public class ClinicRegistrationServiceTests
{
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<UserManager<IdentityUser>> _users;
    private readonly Mock<IKnowledgeSettingsService> _knowledge = new();
    private readonly Mock<ISubscriptionService> _subscriptions = new();
    private readonly Mock<IConfigManager> _config = new();

    public ClinicRegistrationServiceTests()
    {
        _users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddClaimAsync(It.IsAny<IdentityUser>(), It.IsAny<Claim>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        _config.SetupGet(c => c.BillingCurrency).Returns("USD");
        _config.SetupGet(c => c.BillingSignupPlanCode).Returns(" starter ");
        _config.SetupGet(c => c.BillingEnabled).Returns(true);
    }

    private ClinicRegistrationService Sut() => new(_clinics.Object, _accounts.Object, _uow.Object, _users.Object, _knowledge.Object, NullLogger<ClinicRegistrationService>.Instance, _subscriptions.Object, _config.Object);

    private static RegisterClinicRequest Req(string? name = "Ann Lee", string? clinic = "Glow Clinic", string? email = "ann@x.com", string? pw = "Passw0rd!", string? confirm = "Passw0rd!") =>
        new(name, clinic, email, pw, confirm);

    [Theory]
    [InlineData(" ", "Glow", "a@x.com", "p", "p", "Full name")]
    [InlineData("Ann", " ", "a@x.com", "p", "p", "Clinic name")]
    [InlineData("Ann", "Glow", "not-an-email", "p", "p", "valid email")]
    [InlineData("Ann", "Glow", "a@x.com", "", "", "Password is required")]
    [InlineData("Ann", "Glow", "a@x.com", "p1", "p2", "don't match")]
    public async Task Invalid_input_is_rejected_before_touching_the_database(string name, string clinic, string email, string pw, string confirm, string message)
    {
        var r = await Sut().RegisterAsync(new RegisterClinicRequest(name, clinic, email, pw, confirm));
        Assert.False(r.Succeeded);
        Assert.Contains(message, Assert.Single(r.Errors));
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Overlong_names_are_rejected()
    {
        Assert.False((await Sut().RegisterAsync(Req(name: new string('n', 201)))).Succeeded);
        Assert.False((await Sut().RegisterAsync(Req(clinic: new string('c', 201)))).Succeeded);
    }

    [Fact]
    public async Task Registration_creates_user_clinic_membership_billing_account_and_a_signup_plan()
    {
        Clinic? clinic = null;
        _clinics.Setup(c => c.Add(It.IsAny<Clinic>())).Callback<Clinic>(c => clinic = c);
        ClinicUser? membership = null;
        _clinics.Setup(c => c.AddMembership(It.IsAny<ClinicUser>())).Callback<ClinicUser>(m => membership = m);
        BillingAccount? account = null;
        _accounts.Setup(a => a.Add(It.IsAny<BillingAccount>())).Callback<BillingAccount>(a => account = a);

        var r = await Sut().RegisterAsync(Req(clinic: "  Glow Clinic  "));

        Assert.True(r.Succeeded);
        Assert.Equal(("Glow Clinic", "glow-clinic", "ann@x.com", "UTC", true), (clinic!.Name, clinic.Slug, clinic.Email, clinic.Timezone, clinic.IsActive));
        Assert.Equal((clinic.Id, r.User!.Id), (membership!.ClinicId, membership.UserId));
        Assert.Equal(("USD", clinic.Id), (account!.Currency, account.ClinicId));
        _users.Verify(u => u.AddClaimAsync(r.User, It.Is<Claim>(c => c.Type == ClinicRegistrationService.FullNameClaimType && c.Value == "Ann Lee")), Times.Once);
        _knowledge.Verify(k => k.GetAsync(clinic.Id, It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _subscriptions.Verify(s => s.StartAsync(It.Is<StartSubscriptionRequest>(q => q.ClinicId == clinic.Id && q.PlanCode == "starter" && !q.ChargeFirstPeriod), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_signup_plan_when_billing_is_off_or_unconfigured_and_a_plan_failure_does_not_fail_registration()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(false);
        Assert.True((await Sut().RegisterAsync(Req())).Succeeded);
        _subscriptions.Verify(s => s.StartAsync(It.IsAny<StartSubscriptionRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        _config.SetupGet(c => c.BillingEnabled).Returns(true);
        _subscriptions.Setup(s => s.StartAsync(It.IsAny<StartSubscriptionRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no plan"));
        Assert.True((await Sut().RegisterAsync(Req())).Succeeded);
    }

    [Fact]
    public async Task Identity_errors_roll_back_and_are_returned()
    {
        _users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));
        var r = await Sut().RegisterAsync(Req());
        Assert.Equal("Password too weak", Assert.Single(r.Errors));
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);
        _clinics.Verify(c => c.Add(It.IsAny<Clinic>()), Times.Never);

        _users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddClaimAsync(It.IsAny<IdentityUser>(), It.IsAny<Claim>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "claim failed" }));
        Assert.Equal("claim failed", Assert.Single((await Sut().RegisterAsync(Req())).Errors));
    }

    [Fact]
    public async Task Unexpected_errors_roll_back_with_a_friendly_message()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var r = await Sut().RegisterAsync(Req());
        Assert.False(r.Succeeded);
        Assert.Contains("Nothing was saved", r.Errors[0]);
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain("db down", r.Errors[0]);
    }

    [Fact]
    public async Task A_unique_race_retries_with_a_random_suffix_and_gives_up_after_three_attempts()
    {
        _uow.Setup(u => u.IsDuplicateRecord(It.IsAny<Exception>())).Returns(true);
        var slugs = new List<string>();
        _clinics.Setup(c => c.Add(It.IsAny<Clinic>())).Callback<Clinic>(c => slugs.Add(c.Slug));
        var calls = 0;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(() => ++calls < 3 ? throw new InvalidOperationException("dup") : Task.FromResult(1));
        var ok = await Sut().RegisterAsync(Req());
        Assert.True(ok.Succeeded);
        Assert.Equal(3, slugs.Count);
        Assert.Equal("glow-clinic", slugs[0]);
        Assert.Matches("^glow-clinic-[0-9a-f]{6}$", slugs[2]);

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("dup"));
        var failed = await Sut().RegisterAsync(Req());
        Assert.False(failed.Succeeded);
        Assert.Contains("try again", failed.Errors[0]);
    }

    [Fact]
    public async Task Taken_slugs_get_a_numeric_suffix()
    {
        _clinics.Setup(c => c.SlugExistsAsync("glow-clinic", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinics.Setup(c => c.SlugExistsAsync("glow-clinic-2", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Clinic? clinic = null;
        _clinics.Setup(c => c.Add(It.IsAny<Clinic>())).Callback<Clinic>(c => clinic = c);
        await Sut().RegisterAsync(Req());
        Assert.Equal("glow-clinic-3", clinic!.Slug);
    }

    [Fact]
    public async Task External_registration_links_the_provider_and_confirms_the_email()
    {
        IdentityUser? created = null;
        _users.Setup(u => u.CreateAsync(It.IsAny<IdentityUser>())).Callback<IdentityUser>(u => created = u).ReturnsAsync(IdentityResult.Success);
        var r = await Sut().RegisterExternalAsync(new RegisterExternalClinicRequest(" ", "Glow", "g@x.com", "Google", "key-1", "Google"));
        Assert.True(r.Succeeded);
        Assert.True(created!.EmailConfirmed);
        _users.Verify(u => u.AddLoginAsync(created, It.Is<UserLoginInfo>(l => l.LoginProvider == "Google" && l.ProviderKey == "key-1")), Times.Once);
        _users.Verify(u => u.AddClaimAsync(created, It.Is<Claim>(c => c.Value == "g@x.com")), Times.Once); // name falls back to the email
    }

    [Fact]
    public async Task External_registration_validates_and_surfaces_login_link_failures()
    {
        Assert.False((await Sut().RegisterExternalAsync(new RegisterExternalClinicRequest("n", " ", "g@x.com", "Google", "k", null))).Succeeded);
        Assert.False((await Sut().RegisterExternalAsync(new RegisterExternalClinicRequest("n", "c", "bad", "Google", "k", null))).Succeeded);
        Assert.False((await Sut().RegisterExternalAsync(new RegisterExternalClinicRequest("n", new string('c', 201), "g@x.com", "Google", "k", null))).Succeeded);
        _users.Setup(u => u.AddLoginAsync(It.IsAny<IdentityUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "already linked" }));
        Assert.Equal("already linked", Assert.Single((await Sut().RegisterExternalAsync(new RegisterExternalClinicRequest("n", "c", "g@x.com", "Google", "k", null))).Errors));
    }

    [Theory]
    [InlineData("Glow Clinic", "glow-clinic")]
    [InlineData("  Café Élégance!! ", "cafe-elegance")]
    [InlineData("---", "clinic")]
    [InlineData("عيادة", "clinic")]
    [InlineData("A & B  Surgery #1", "a-b-surgery-1")]
    public void Slugify(string name, string expected) => Assert.Equal(expected, ClinicRegistrationService.Slugify(name));

    [Fact]
    public void Slugify_is_capped_at_60_chars_without_a_trailing_dash() =>
        Assert.True(ClinicRegistrationService.Slugify(new string('a', 59) + " b").Length <= 60 && !ClinicRegistrationService.Slugify(new string('a', 59) + " b").EndsWith('-'));
}

public class TikTokIntegrationServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITikTokIntegrationRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ITikTokProviderClient> _provider = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IConfigManager> _config = new();
    private TikTokIntegration? _row;

    public TikTokIntegrationServiceTests()
    {
        _config.SetupGet(c => c.IntegrationsTokenRefreshMarginMinutes).Returns(5);
        _repo.Setup(r => r.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _row);
        _repo.Setup(r => r.Add(It.IsAny<TikTokIntegration>())).Callback<TikTokIntegration>(t => _row = t);
    }

    private TikTokIntegrationService Sut() => new(_repo.Object, _uow.Object, _provider.Object, _notifications.Object, _config.Object);

    private TikTokIntegration Connected(DateTimeOffset? expires = null) => _row = new TikTokIntegration
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, Status = TikTokIntegrationStatus.Connected, AccessToken = "a", RefreshToken = "r",
        TokenExpiresAt = expires ?? DateTimeOffset.UtcNow.AddHours(1), RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30), IsHealthy = true
    };

    [Fact]
    public async Task Get_returns_a_placeholder_when_not_connected()
    {
        var r = await Sut().GetAsync(_clinicId);
        Assert.Equal((Guid.Empty, TikTokIntegrationStatus.Disconnected), (r.Id, r.Status));
    }

    [Fact]
    public async Task Get_with_a_valid_token_does_not_call_tiktok()
    {
        Connected();
        Assert.Equal(TikTokIntegrationStatus.Connected, (await Sut().GetAsync(_clinicId)).Status);
        _provider.Verify(p => p.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_refreshes_an_expiring_token()
    {
        Connected(DateTimeOffset.UtcNow.AddMinutes(2));
        var newExpiry = DateTimeOffset.UtcNow.AddHours(2);
        _provider.Setup(p => p.RefreshAccessTokenAsync("r", It.IsAny<CancellationToken>())).ReturnsAsync(new TikTokOAuthTokenResult("a2", null, newExpiry, null));
        await Sut().GetAsync(_clinicId);
        Assert.Equal(("a2", "r", newExpiry), (_row!.AccessToken, _row.RefreshToken, _row.TokenExpiresAt));
        Assert.True(_row.IsHealthy);
    }

    [Theory]
    [InlineData("no-refresh")]
    [InlineData("refresh-expired")]
    [InlineData("refresh-fails")]
    public async Task Unrefreshable_tokens_mark_the_connection_unhealthy_and_notify_once(string scenario)
    {
        Connected(DateTimeOffset.UtcNow.AddSeconds(30));
        switch (scenario)
        {
            case "no-refresh": _row!.RefreshToken = null; break;
            case "refresh-expired": _row!.RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-1); break;
            default: _provider.Setup(p => p.RefreshAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("invalid_grant")); break;
        }
        await Sut().GetAsync(_clinicId);
        Assert.False(_row!.IsHealthy);
        Assert.NotNull(_row.LastProblemMessage);
        _notifications.Verify(n => n.CreateAsync(_clinicId, NotificationType.IntegrationUnhealthy, "TikTok connection needs attention", It.IsAny<string?>(), null, null, null, null, "/settings/integrations", It.IsAny<CancellationToken>()), Times.Once);
        await Sut().GetAsync(_clinicId);
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connect_flow_pending_then_connected_or_error()
    {
        await Sut().RequestConnectAsync(_clinicId);
        Assert.Equal(TikTokIntegrationStatus.Pending, _row!.Status);

        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var r = await Sut().CompleteConnectAsync(_clinicId, new TikTokOAuthTokenResult("a", "r", expires, expires.AddDays(30)), new TikTokAccountInfo("open1", "union1", "Clinic TT", "http://img"));
        Assert.Equal((TikTokIntegrationStatus.Connected, "Clinic TT", true), (r.Status, r.DisplayName, r.IsHealthy));
        Assert.Equal(("open1", "union1", "r"), (_row.OpenId, _row.UnionId, _row.RefreshToken));

        await Sut().CompleteConnectAsync(_clinicId, new TikTokOAuthTokenResult("a2", null, expires, null), new TikTokAccountInfo("open1", null, "Clinic TT", null));
        Assert.Equal(("union1", "r"), (_row.UnionId, _row.RefreshToken)); // kept when the provider omits them

        await Sut().FailConnectAsync(_clinicId, "denied");
        Assert.Equal((TikTokIntegrationStatus.Error, "denied"), (_row.Status, _row.LastProblemMessage));
    }

    [Fact]
    public async Task Disconnect_revokes_and_clears_everything()
    {
        await Sut().DisconnectAsync(_clinicId);
        Connected();
        _row!.OpenId = "o";
        await Sut().DisconnectAsync(_clinicId);
        _provider.Verify(p => p.RevokeAsync("a", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((TikTokIntegrationStatus.Disconnected, null, null, null), (_row.Status, _row.AccessToken, _row.RefreshToken, _row.OpenId));
    }

}
