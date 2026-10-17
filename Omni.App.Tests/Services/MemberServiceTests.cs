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
        new(clients, Mock.Of<IUnitOfWork>(), NewUserManager().Object, Mock.Of<IEntitlementService>(), TimeProvider.System);

    private MemberService Sut() => new(_clients.Object, _uow.Object, _users.Object, _entitlements.Object, TimeProvider.System);

    private UserAccess Owner => new("owner", _clientId, UserRole.Client, null, "Olga", null);
    private UserAccess Member => new("m0", _clientId, UserRole.Member, null, "Max", null);

    private static AddMemberRequest Req(string? name = "Ann Lee", string? email = "ann@x.com", string? pw = "Passw0rd") => new(name, email, pw);

    [Fact]
    public async Task Only_the_client_role_manages_members()
    {
        Assert.Equal("members.errors.clientOnly", (await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().AddAsync(Member, Req()))).Message);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sut().SetActiveAsync(Member, "m1", false));
        _users.Verify(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(" ", "ann@x.com", "Passw0rd", "members.errors.nameRequired")]
    [InlineData("Ann", "not-an-email", "Passw0rd", "members.errors.emailInvalid")]
    [InlineData("Ann", "Ann <ann@x.com>", "Passw0rd", "members.errors.emailInvalid")]
    [InlineData("Ann", "ann@x.com", "", "members.errors.passwordRequired")]
    public async Task Adding_checks_the_input(string name, string email, string pw, string key)
    {
        Assert.Equal(key, (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddAsync(Owner, Req(name, email, pw)))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddAsync(Owner, Req(new string('a', 201))));
        _users.Verify(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task An_email_that_already_signs_in_is_refused()
    {
        _users.Setup(u => u.FindByNameAsync("ann@x.com")).ReturnsAsync(new AppUser { Role = UserRole.Admin });
        Assert.Equal("members.errors.emailTaken", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddAsync(Owner, Req()))).Message);
    }

    [Fact]
    public async Task Adding_beyond_the_seats_is_refused_before_anything_is_created()
    {
        _entitlements.Setup(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxUsers, "Your plan allows 2 seats."));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().AddAsync(Owner, Req()));
        _users.Verify(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Adding_creates_a_member_user_and_its_membership_in_one_transaction()
    {
        AppUser? created = null;
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), "Passw0rd")).Callback<AppUser, string>((u, _) => created = u).ReturnsAsync(IdentityResult.Success);
        ClientUser? membership = null;
        _clients.Setup(c => c.AddMembership(It.IsAny<ClientUser>())).Callback<ClientUser>(m => membership = m);

        var added = await Sut().AddAsync(Owner, Req(" Ann Lee ", " ann@x.com "));

        Assert.Equal((UserRole.Member, "Ann Lee", "ann@x.com", "ann@x.com", true), (created!.Role, created.FullName, created.Email, created.UserName, created.IsActive));
        Assert.Equal((_clientId, created.Id, true), (membership!.ClientId, membership.UserId, membership.IsActive));
        Assert.Equal((created.Id, UserRole.Member, true), (added.UserId, added.Role, added.IsActive));
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxUsers, 3, It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_password_the_rules_refuse_rolls_back()
    {
        _users.Setup(u => u.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit ('0'-'9')." }));
        Assert.Contains("digit", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().AddAsync(Owner, Req(pw: "weak")))).Message);
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);
        _clients.Verify(c => c.AddMembership(It.IsAny<ClientUser>()), Times.Never);
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
