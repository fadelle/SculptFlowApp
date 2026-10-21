using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Omni.App.Tests.Services;

/// <summary>Milestone C S2: member invites. Only the client role invites; an open invite holds a seat; a link works once
/// and is replaced on resend; a failed email keeps nothing; accepting creates the member and its membership together.</summary>
public class InviteServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IAccountTokenRepository> _tokens = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<UserManager<AppUser>> _users = new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IAccountEmailService> _emails = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly List<AccountToken> _added = new();
    private readonly List<string> _links = new();
    private readonly DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly Client _client;

    public InviteServiceTests()
    {
        _client = new Client { Id = _clientId, Name = "Glow", DefaultLanguage = "ar", BusinessTypeCode = TextKit.General, IsActive = true };
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(_client);
        _clients.Setup(c => c.CountSeatsInUseAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _config.SetupGet(c => c.AccountsInviteDays).Returns(7);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _tokens.Setup(t => t.Add(It.IsAny<AccountToken>())).Callback<AccountToken>(_added.Add);
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _emails.Setup(e => e.SendInviteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Client>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, Client, string?, string, int, CancellationToken>((_, _, _, _, _, link, _, _) => _links.Add(link));
    }

    private InviteService Sut()
    {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(_now);
        return new InviteService(_tokens.Object, _clients.Object, _uow.Object, _users.Object, _entitlements.Object, _emails.Object, _config.Object,
            new ConfigurationBuilder().Build(), time.Object, NullLogger<InviteService>.Instance);
    }

    private UserAccess Owner => new("owner", _clientId, UserRole.Client, "fr", "Olga", null);
    private UserAccess Member => new("m0", _clientId, UserRole.Member, null, "Max", null);

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    /// <summary>A stored invite for <paramref name="raw"/>.</summary>
    private AccountToken Stored(string raw, string email = "ann@x.com")
    {
        var invite = new AccountToken
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.Invite, ClientId = _clientId, Email = email, FullName = "Ann",
            TokenHash = AccountTokenCodec.Hash(raw)!, ExpiresAt = _now.AddDays(3), CreatedAt = _now.AddDays(-4)
        };
        _tokens.Setup(t => t.GetByHashAsync(invite.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        _tokens.Setup(t => t.GetInviteAsync(_clientId, invite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        _tokens.Setup(t => t.GetInviteAsync(invite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invite);
        return invite;
    }

    // ---- inviting ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Only_the_client_role_invites_resends_or_cancels()
    {
        Assert.Equal("members.errors.clientOnly",
            (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().InviteAsync(Member, new("ann@x.com", null), "http://localhost"))).Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().ResendAsync(Member, Guid.NewGuid(), "http://localhost"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().RevokeAsync(Member, Guid.NewGuid()));
        Assert.Empty(_added);
    }

    [Theory]
    [InlineData("not-an-email", null, "members.errors.emailInvalid")]
    [InlineData("taken@x.com", null, "members.errors.emailTaken")]
    [InlineData("again@x.com", null, "members.errors.alreadyInvited")]
    public async Task Inviting_checks_the_address(string email, string? name, string key)
    {
        _users.Setup(u => u.FindByEmailAsync("taken@x.com")).ReturnsAsync(new AppUser());
        _tokens.Setup(t => t.HasOpenInviteAsync(_clientId, "again@x.com", _now, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Equal(key, (await Assert.ThrowsAsync<ArgumentException>(() => Sut().InviteAsync(Owner, new(email, name), "http://localhost"))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().InviteAsync(Owner, new("ok@x.com", new string('a', 201)), "http://localhost"));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Open_invites_count_as_seats_when_inviting()
    {
        _tokens.Setup(t => t.CountOpenInvitesAsync(_clientId, _now, null, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _entitlements.Setup(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 4, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxUsers, "Your plan allows 3 seats."));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().InviteAsync(Owner, new("ann@x.com", null), "http://localhost"));
        Assert.Empty(_added);
        _emails.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invite_is_stored_hashed_for_seven_days_and_emailed_in_one_transaction()
    {
        var invite = await Sut().InviteAsync(Owner, new(" Ann@X.com ", " Ann Lee "), "http://localhost:5000");

        var stored = Assert.Single(_added);
        Assert.Equal((AccountTokenPurpose.Invite, _clientId, "Ann@X.com", "Ann Lee", "owner", _now.AddDays(7), (string?)null),
            (stored.Purpose, stored.ClientId!.Value, stored.Email, stored.FullName, stored.CreatedByUserId, stored.ExpiresAt, stored.UserId));
        var link = Assert.Single(_links);
        Assert.StartsWith("http://localhost:5000/Account/AcceptInvite?token=", link);
        Assert.Equal(stored.TokenHash, AccountTokenCodec.Hash(TokenOf(link)));
        _emails.Verify(e => e.SendInviteAsync("Ann@X.com", "Ann Lee", "Olga", _client, "fr", link, 7, It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((stored.Id, false), (invite.Id, invite.IsExpired));
    }

    [Fact]
    public async Task A_failed_email_keeps_no_invite()
    {
        _emails.Setup(e => e.SendInviteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Client>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmailSendException("Brevo down"));
        await Assert.ThrowsAsync<EmailSendException>(() => Sut().InviteAsync(Owner, new("ann@x.com", null), "http://localhost"));
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);
    }

    // ---- resend, cancel, list -------------------------------------------------------------------------------------

    [Fact]
    public async Task Resending_replaces_the_link_and_an_expired_invite_needs_a_seat_again()
    {
        var old = Stored("old-raw");
        await Sut().ResendAsync(Owner, old.Id, "http://localhost");
        Assert.Equal(_now, old.RevokedAt);
        var fresh = Assert.Single(_added);
        Assert.Equal((old.Email, old.FullName, _now.AddDays(7)), (fresh.Email, fresh.FullName, fresh.ExpiresAt));
        Assert.NotEqual(old.TokenHash, fresh.TokenHash);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var expired = Stored("expired-raw", "bo@x.com");
        expired.ExpiresAt = _now.AddMinutes(-1);
        await Sut().ResendAsync(Owner, expired.Id, "http://localhost");
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3, It.IsAny<CancellationToken>()), Times.Once);
        _tokens.Verify(t => t.CountOpenInvitesAsync(_clientId, _now, expired.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accepted_cancelled_or_foreign_invites_cannot_be_resent_or_cancelled()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ResendAsync(Owner, Guid.NewGuid(), "http://localhost"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().RevokeAsync(Owner, Guid.NewGuid()));
        var used = Stored("used");
        used.UsedAt = _now.AddDays(-1);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ResendAsync(Owner, used.Id, "http://localhost"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().RevokeAsync(Owner, used.Id));
        var revoked = Stored("revoked", "c@x.com");
        revoked.RevokedAt = _now.AddDays(-1);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ResendAsync(Owner, revoked.Id, "http://localhost"));
    }

    [Fact]
    public async Task Cancelling_stops_the_link_once_and_the_portal_learns_the_client()
    {
        var invite = Stored("raw");
        await Sut().RevokeAsync(Owner, invite.Id);
        Assert.Equal(_now, invite.RevokedAt);
        await Sut().RevokeAsync(Owner, invite.Id); // already cancelled: nothing more to save
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var other = Stored("raw-2", "d@x.com");
        Assert.Equal(_clientId, await Sut().RevokeAnyAsync(other.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().RevokeAnyAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_list_marks_expired_invites()
    {
        var open = new AccountToken { Id = Guid.NewGuid(), Email = "a@x.com", CreatedAt = _now, ExpiresAt = _now.AddDays(1) };
        var expired = new AccountToken { Id = Guid.NewGuid(), Email = "b@x.com", CreatedAt = _now.AddDays(-8), ExpiresAt = _now.AddDays(-1) };
        _tokens.Setup(t => t.ListPendingInvitesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync([open, expired]);
        var rows = await Sut().ListAsync(_clientId);
        Assert.Equal([false, true], rows.Select(r => r.IsExpired));
    }

    // ---- accepting --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_landing_shows_a_working_link_only()
    {
        Stored("raw");
        Assert.Equal(new InviteLanding("Glow", "ann@x.com", "Ann"), await Sut().GetLandingAsync("raw"));
        Assert.Null(await Sut().GetLandingAsync("other"));
        Assert.Null(await Sut().GetLandingAsync(null));

        _client.IsActive = false;
        Assert.Null(await Sut().GetLandingAsync("raw"));
    }

    [Theory]
    [InlineData(" ", "Passw0rd!", "Passw0rd!", "acceptInvite.errors.nameRequired")]
    [InlineData("Ann", "", "", "resetPassword.errors.missingFields")]
    [InlineData("Ann", "Passw0rd!", "Passw0rd?", "resetPassword.errors.mismatch")]
    public async Task Accepting_checks_the_form_first(string name, string password, string confirm, string key)
    {
        Stored("raw");
        Assert.Equal(key, (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AcceptAsync("raw", name, password, confirm))).Message);
        _users.Verify(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task A_dead_link_or_a_taken_email_refuses_the_accept()
    {
        var invite = Stored("raw");
        invite.ExpiresAt = _now;
        Assert.Equal("acceptInvite.errors.linkInvalid",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().AcceptAsync("raw", "Ann", "Passw0rd!", "Passw0rd!"))).Message);

        invite.ExpiresAt = _now.AddDays(1);
        _users.Setup(u => u.FindByNameAsync("ann@x.com")).ReturnsAsync(new AppUser());
        Assert.Equal("members.errors.emailTaken",
            (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AcceptAsync("raw", "Ann", "Passw0rd!", "Passw0rd!"))).Message);
        _users.Verify(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Accepting_creates_a_member_and_its_membership_and_uses_the_link()
    {
        var invite = Stored("raw");
        AppUser? created = null;
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), "Passw0rd!")).Callback<AppUser, string>((u, _) => created = u).ReturnsAsync(IdentityResult.Success);
        ClientUser? membership = null;
        _clients.Setup(c => c.AddMembership(It.IsAny<ClientUser>())).Callback<ClientUser>(m => membership = m);

        var user = await Sut().AcceptAsync("raw", " Ann Lee ", "Passw0rd!", "Passw0rd!");

        Assert.Same(created, user);
        Assert.Equal((UserRole.Member, "Ann Lee", "ann@x.com", "ann@x.com", true, true), (user.Role, user.FullName, user.Email, user.UserName, user.EmailConfirmed, user.IsActive));
        Assert.Equal((_clientId, user.Id, true), (membership!.ClientId, membership.UserId, membership.IsActive));
        Assert.Equal((_now, user.Id), (invite.UsedAt!.Value, invite.UserId));
        // The invite's own seat is not counted twice.
        _tokens.Verify(t => t.CountOpenInvitesAsync(_clientId, _now, invite.Id, It.IsAny<CancellationToken>()), Times.Once);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3, It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_password_the_rules_refuse_rolls_back_and_keeps_the_link()
    {
        var invite = Stored("raw");
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit ('0'-'9')." }));
        Assert.Contains("digit", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AcceptAsync("raw", "Ann", "weak-pw", "weak-pw"))).Message);
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.AddMembership(It.IsAny<ClientUser>()), Times.Never);
        Assert.Null(invite.UsedAt);
    }
}
