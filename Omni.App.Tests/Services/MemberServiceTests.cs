using Microsoft.AspNetCore.Identity;

namespace Omni.App.Tests.Services;

/// <summary>Members of a client: only the client role manages them, seats (max_users) and the one-active-membership
/// rule hold on every activation.</summary>
public class MemberServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<UserManager<AppUser>> _users = NewUserManager();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IAccountTokenRepository> _tokens = new();

    public MemberServiceTests()
    {
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _clients.Setup(c => c.CountSeatsInUseAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
    }

    private static Mock<UserManager<AppUser>> NewUserManager() =>
        new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    /// <summary>A service over these clients only (for read tests elsewhere).</summary>
    internal static MemberService Sut(IClientRepository clients) =>
        new(clients, Mock.Of<IUnitOfWork>(), NewUserManager().Object, Mock.Of<IEntitlementService>(), Mock.Of<IAccountTokenRepository>(), TimeProvider.System);

    private MemberService Sut() => new(_clients.Object, _uow.Object, _users.Object, _entitlements.Object, _tokens.Object, TimeProvider.System);

    private UserAccess Owner => new("owner", _clientId, UserRole.Client, null, "Olga", null);
    private UserAccess Member => new("m0", _clientId, UserRole.Member, null, "Max", null);

    [Fact]
    public async Task Anyone_sets_their_own_language_and_only_dashboard_languages_are_accepted()
    {
        var user = new AppUser { Id = "m0" };
        _users.Setup(u => u.FindByIdAsync("m0")).ReturnsAsync(user);

        await Sut().SetOwnLanguageAsync(Member, " AR ");
        Assert.Equal("ar", user.Language);
        await Sut().SetOwnLanguageAsync(Member, "ar"); // no change, no save
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await Sut().SetOwnLanguageAsync(Member, " "); // back to the client's default
        Assert.Null(user.Language);
        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetOwnLanguageAsync(Member, "de"));
        Assert.Equal("There is no dashboard language 'de'.", TextKit.English(unknown));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Only_the_client_role_manages_members()
    {
        Assert.Equal("members.errors.clientOnly", (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().SetActiveAsync(Member, "m1", false))).Message);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Open_invites_hold_seats_when_a_member_is_reactivated()
    {
        var m = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, UserId = "m1", IsActive = false };
        _tokens.Setup(t => t.CountOpenInvitesAsync(_clientId, It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        await Sut().SetMembershipActiveAsync(m, true);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivating_refuses_yourself_strangers_and_the_client_user()
    {
        Assert.Equal("members.errors.notYourself", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetActiveAsync(Owner, "owner", false))).Message);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetActiveAsync(Owner, "stranger", false));

        var ownerTwo = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, UserId = "c2", IsActive = true };
        _clients.Setup(c => c.GetMembershipAsync(_clientId, "c2", It.IsAny<CancellationToken>())).ReturnsAsync(ownerTwo);
        _users.Setup(u => u.FindByIdAsync("c2")).ReturnsAsync(new AppUser { Id = "c2", Role = UserRole.Client });
        Assert.Equal("members.errors.onlyMembers", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetActiveAsync(Owner, "c2", false))).Message);
        Assert.True(ownerTwo.IsActive);
    }

    [Fact]
    public async Task Deactivating_a_member_saves_and_reactivating_checks_seats()
    {
        var m = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, UserId = "m1", IsActive = true };
        _clients.Setup(c => c.GetMembershipAsync(_clientId, "m1", It.IsAny<CancellationToken>())).ReturnsAsync(m);
        _users.Setup(u => u.FindByIdAsync("m1")).ReturnsAsync(new AppUser { Id = "m1", Role = UserRole.Member });

        await Sut().SetActiveAsync(Owner, "m1", false);
        Assert.False(m.IsActive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        _entitlements.Setup(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxUsers, "Your plan allows 2 seats."));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SetActiveAsync(Owner, "m1", true));
        Assert.False(m.IsActive);

        _entitlements.Reset();
        await Sut().SetActiveAsync(Owner, "m1", true);
        Assert.True(m.IsActive);
    }

    [Fact]
    public async Task A_user_active_in_another_client_cannot_be_reactivated()
    {
        var m = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, UserId = "m1", IsActive = false };
        _clients.Setup(c => c.HasOtherActiveMembershipAsync("m1", m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Equal("members.errors.otherClient", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetMembershipActiveAsync(m, true))).Message);
        Assert.False(m.IsActive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unchanged_membership_is_not_saved()
    {
        var m = new ClientUser { Id = Guid.NewGuid(), ClientId = _clientId, UserId = "m1", IsActive = true };
        await Sut().SetMembershipActiveAsync(m, true);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Listing_maps_role_and_last_sign_in()
    {
        var at = DateTimeOffset.UtcNow;
        _clients.Setup(c => c.ListMembersAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientMemberRow> { new("m1", "m@x.com", "Mo", UserRole.Member, true, at, at) });
        var row = Assert.Single(await Sut().ListAsync(_clientId));
        Assert.Equal(new MemberResponse("m1", "Mo", "m@x.com", UserRole.Member, true, at, at), row);
    }
}
