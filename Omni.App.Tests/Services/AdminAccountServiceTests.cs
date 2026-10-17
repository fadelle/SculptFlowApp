using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Services.PlatformAdmin;

namespace Omni.App.Tests.Services;

/// <summary>Admin user management rules (the sign-in itself runs on Postgres in AccountSignInTests).</summary>
public class AdminAccountServiceTests
{
    private readonly Mock<UserManager<AppUser>> _users =
        new(Mock.Of<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly Mock<IAdminUserRepository> _admins = new();
    private readonly AppUser _ada = new() { Id = "a1", Email = "ada@x.com", FullName = "Ada", Role = UserRole.Admin, IsActive = true };
    private readonly AppUser _bo = new() { Id = "a2", Email = "bo@x.com", FullName = "Bo", Role = UserRole.Admin, IsActive = true };
    private readonly AppUser _client = new() { Id = "c1", Email = "c@x.com", Role = UserRole.Client, IsActive = true };

    public AdminAccountServiceTests()
    {
        foreach (var u in new[] { _ada, _bo, _client }) _users.Setup(m => m.FindByIdAsync(u.Id)).ReturnsAsync(u);
        _users.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.UpdateAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.HasPasswordAsync(It.IsAny<AppUser>())).ReturnsAsync(true);
        _users.Setup(m => m.RemovePasswordAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.AddPasswordAsync(It.IsAny<AppUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<AppUser>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.SetLockoutEndDateAsync(It.IsAny<AppUser>(), It.IsAny<DateTimeOffset?>())).ReturnsAsync(IdentityResult.Success);
        _admins.Setup(a => a.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
    }

    private AdminAccountService Sut() => new(_users.Object, _admins.Object, TimeProvider.System);

    [Theory]
    [InlineData("not-an-email", "Ann", "Long-enough-1", "valid email")]
    [InlineData("ann@x.com", " ", "Long-enough-1", "name")]
    [InlineData("ann@x.com", "Ann", "Short-1", "at least 10")]
    [InlineData("ann@x.com", "Ann", null, "at least 10")]
    public async Task Creating_checks_the_input(string email, string name, string? password, string expected)
    {
        Assert.Contains(expected, (await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(email, name, password))).Message);
        _users.Verify(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task An_email_any_user_already_has_cannot_become_an_admin()
    {
        _users.Setup(m => m.FindByEmailAsync("c@x.com")).ReturnsAsync(_client);
        Assert.Contains("already exists", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync("c@x.com", "Cy", "Long-enough-1"))).Message);
    }

    [Fact]
    public async Task Creating_makes_an_active_admin_with_a_confirmed_email()
    {
        AppUser? created = null;
        _users.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), "Long-enough-1")).Callback<AppUser, string>((u, _) => created = u).ReturnsAsync(IdentityResult.Success);
        var row = await Sut().CreateAsync(" new@x.com ", " Nia ", "Long-enough-1");
        Assert.Equal((UserRole.Admin, true, true, "new@x.com", "Nia"), (created!.Role, created.IsActive, created.EmailConfirmed, created.UserName, created.FullName));
        Assert.Equal((created.Id, "new@x.com", true), (row.Id, row.Email, row.IsActive));

        _users.Setup(m => m.CreateAsync(It.IsAny<AppUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit." }));
        Assert.Contains("digit", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync("x@x.com", "X", "no-digits-here"))).Message);
    }

    [Fact]
    public async Task Nobody_deactivates_themselves_or_the_last_active_admin()
    {
        Assert.Contains("your own", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetActiveAsync("a1", false, "a1"))).Message);
        _admins.Setup(a => a.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Assert.Contains("At least one admin", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetActiveAsync("a2", false, "a1"))).Message);
        Assert.True(_bo.IsActive);

        _admins.Setup(a => a.CountActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        Assert.False((await Sut().SetActiveAsync("a2", false, "a1")).IsActive);
        Assert.True((await Sut().SetActiveAsync("a2", true, "a1")).IsActive);  // reactivating needs no count
    }

    [Fact]
    public async Task Client_and_member_users_are_not_admins_here()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetActiveAsync("c1", false, "a1"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetPasswordAsync("c1", "Long-enough-1"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SetActiveAsync("missing", false, "a1"));
        Assert.Null(await Sut().GetActiveAsync("c1"));
        Assert.Equal(new AdminIdentity("a1", "Ada", "ada@x.com"), await Sut().GetActiveAsync("a1"));
        _ada.IsActive = false;
        Assert.Null(await Sut().GetActiveAsync("a1"));
    }

    [Fact]
    public async Task A_password_reset_checks_the_rules_and_clears_the_lockout()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetPasswordAsync("a2", "short"));
        var validator = new Mock<IPasswordValidator<AppUser>>();
        validator.Setup(v => v.ValidateAsync(_users.Object, _bo, "longbutnodigits")).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "needs a digit" }));
        validator.Setup(v => v.ValidateAsync(_users.Object, _bo, "Long-enough-1")).ReturnsAsync(IdentityResult.Success);
        _users.Object.PasswordValidators.Add(validator.Object);
        Assert.Contains("needs a digit", (await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetPasswordAsync("a2", "longbutnodigits"))).Message);
        _users.Verify(m => m.RemovePasswordAsync(_bo), Times.Never);

        await Sut().SetPasswordAsync("a2", "Long-enough-1");
        _users.Verify(m => m.AddPasswordAsync(_bo, "Long-enough-1"), Times.Once);
        _users.Verify(m => m.ResetAccessFailedCountAsync(_bo), Times.Once);
        _users.Verify(m => m.SetLockoutEndDateAsync(_bo, null), Times.Once);
    }
}
