using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Business.Managers;
using Omni.App.Business.Services.PlatformAdmin;
using Omni.App.Common.Extensions;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.PlatformAdmin;

namespace Omni.App.Tests.Repositories;

/// <summary>
/// The sign-in matrix on the real Identity wiring (AddOmniIdentity) and Postgres: role × app × active × membership ×
/// locked. The main app (AppSignInManager) lets in only active client and member users with an active membership; the
/// admin portal (AdminAccountService) only active admins. Both share Identity's lockout (5 failures, 15 minutes).
/// </summary>
public class AccountSignInTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private const string Password = "Passw0rd-123";

    private ServiceProvider Services(Mock<IAuthenticationService>? auth = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddAuthentication();
        services.AddScoped(_ => TextKit.Localizer());
        services.AddOmniIdentity();
        services.AddSingleton((auth ?? new Mock<IAuthenticationService>()).Object);
        return services.BuildServiceProvider();
    }

    /// <summary>Creates a user through UserManager (real password hash) and, for client/member users, a membership.</summary>
    private async Task<AppUser> CreateAsync(IServiceProvider sp, string role, bool active = true, bool? membershipActive = true)
    {
        var client = MakeClient();
        await SeedAsync(client);
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"{role}-{Guid.NewGuid():N}@x.com";
        var user = new AppUser { UserName = email, Email = email, Role = role, FullName = "Pat " + role, IsActive = active, CreatedAt = Now, UpdatedAt = Now };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        if (membershipActive is { } isActive)
            await SeedAsync(new ClientUser { Id = Guid.NewGuid(), ClientId = client.Id, UserId = user.Id, IsActive = isActive, CreatedAt = Now, UpdatedAt = Now });
        return user;
    }

    private static async Task<SignInResult> MainAppSignInAsync(IServiceProvider sp, string userId, string password)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
        Assert.IsType<AppSignInManager>(signIn);
        return await signIn.CheckPasswordSignInAsync((await users.FindByIdAsync(userId))!, password, lockoutOnFailure: true);
    }

    private static async Task<AdminIdentity?> AdminSignInAsync(IServiceProvider sp, string? email, string password)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAdminAccountService>().SignInAsync(email, password);
    }

    [PostgresTheory]
    [InlineData(UserRole.Client, true, true, true)]
    [InlineData(UserRole.Member, true, true, true)]
    [InlineData(UserRole.Admin, true, null, false)]    // an admin never uses the main app
    [InlineData(UserRole.Admin, true, true, false)]    // not even with a membership row by mistake
    [InlineData(UserRole.Client, false, true, false)]  // deactivated user
    [InlineData(UserRole.Member, true, false, false)]  // membership deactivated by the client
    [InlineData(UserRole.Member, true, null, false)]   // no membership at all
    public async Task Main_app_sign_in_accepts_only_active_client_and_member_users(string role, bool active, bool? membership, bool allowed)
    {
        await using var sp = Services();
        var user = await CreateAsync(sp, role, active, membership);
        var result = await MainAppSignInAsync(sp, user.Id, Password);
        Assert.Equal(allowed, result.Succeeded);
        if (!allowed) Assert.True(result.IsNotAllowed);
    }

    [PostgresTheory]
    [InlineData(UserRole.Admin, true, true)]
    [InlineData(UserRole.Admin, false, false)]   // deactivated admin
    [InlineData(UserRole.Client, true, false)]   // client users never sign in to the portal
    [InlineData(UserRole.Member, true, false)]
    public async Task Admin_portal_sign_in_accepts_only_active_admins(string role, bool active, bool allowed)
    {
        await using var sp = Services();
        var user = await CreateAsync(sp, role, active, role == UserRole.Admin ? null : true);
        var admin = await AdminSignInAsync(sp, user.Email, Password);
        Assert.Equal(allowed, admin is not null);
        if (allowed) Assert.Equal(new AdminIdentity(user.Id, "Pat admin", user.Email), admin);
    }

    [PostgresFact]
    public async Task Admin_sign_in_stamps_the_last_login_and_unknown_or_wrong_answers_nothing()
    {
        await using var sp = Services();
        var user = await CreateAsync(sp, UserRole.Admin, membershipActive: null);
        Assert.Null(await AdminSignInAsync(sp, "nobody-" + user.Email, Password));
        Assert.Null(await AdminSignInAsync(sp, null, Password));
        Assert.Null(await AdminSignInAsync(sp, user.Email, "wrong"));

        await using (var db = NewContext())
        {
            var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            Assert.Equal((1, (DateTimeOffset?)null), (row.AccessFailedCount, row.LastLoginAt));
        }

        Assert.NotNull(await AdminSignInAsync(sp, " " + user.Email!.ToUpperInvariant() + " ", Password)); // trimmed, any case
        await using var after = NewContext();
        var signedIn = await after.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, signedIn.AccessFailedCount);
        Assert.NotNull(signedIn.LastLoginAt);
    }

    [PostgresTheory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Admin)]
    public async Task Five_wrong_passwords_lock_any_account_for_fifteen_minutes(string role)
    {
        await using var sp = Services();
        var user = await CreateAsync(sp, role, membershipActive: role == UserRole.Admin ? null : true);
        for (var i = 0; i < AccountRules.MaxFailedSignIns; i++)
        {
            if (role == UserRole.Admin) Assert.Null(await AdminSignInAsync(sp, user.Email, "wrong"));
            else Assert.False((await MainAppSignInAsync(sp, user.Id, "wrong")).Succeeded);
        }

        // The right password no longer helps while locked.
        if (role == UserRole.Admin) Assert.Null(await AdminSignInAsync(sp, user.Email, Password));
        else Assert.True((await MainAppSignInAsync(sp, user.Id, Password)).IsLockedOut);

        await using var db = NewContext();
        var lockedUntil = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LockoutEnd!.Value;
        Assert.InRange(lockedUntil - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), AccountRules.LockoutDuration);
    }

    /// <summary>The admin portal's old users table (admin.admin_users) hashed with PasswordHasher&lt;AdminUser&gt;; the
    /// data copy moves those hashes as they are, so they must verify as AppUser. Same for an older v3 hash (the demo
    /// seed's, SHA-256 / 100k), which is rehashed on first sign-in.</summary>
    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_hash_from_the_portals_old_admin_table_verifies_as_an_app_user(bool olderFormat)
    {
        await using var sp = Services();
        var user = await CreateAsync(sp, UserRole.Admin, membershipActive: null);
        var (password, hash) = olderFormat
            ? ("Demo-ChangeMe-2026", "AQAAAAEAAYagAAAAEIQ2jaZC5yJN9fYBnHdWfnpVgfoGC5Nt7tJvhk3HWJQZypOiEoLHKESvr0QdACNZkg==")
            : ("Portal-Admin-42", new PasswordHasher<PortalAdminUser>().HashPassword(new PortalAdminUser(), "Portal-Admin-42"));
        await using (var db = NewContext())
        {
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.PasswordHash, hash));
        }

        Assert.NotNull(await AdminSignInAsync(sp, user.Email, password));
        await using var after = NewContext();
        var stored = (await after.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash;
        Assert.Equal(olderFormat, stored != hash); // only the older format is rehashed
    }

    [PostgresFact]
    public async Task A_main_app_sign_in_stamps_the_last_login_and_puts_name_and_role_on_the_cookie()
    {
        var auth = new Mock<IAuthenticationService>();
        ClaimsPrincipal? principal = null;
        auth.Setup(a => a.SignInAsync(It.IsAny<HttpContext>(), IdentityConstants.ApplicationScheme, It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
            .Callback<HttpContext, string?, ClaimsPrincipal, AuthenticationProperties?>((_, _, p, _) => principal = p)
            .Returns(Task.CompletedTask);
        await using var sp = Services(auth);
        var user = await CreateAsync(sp, UserRole.Member);

        using (var scope = sp.CreateScope())
        {
            var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<AppUser>>();
            signIn.Context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var result = await signIn.PasswordSignInAsync(user.UserName!, Password, isPersistent: true, lockoutOnFailure: true);
            Assert.True(result.Succeeded);
        }

        var cookie = Assert.IsType<ClaimsPrincipal>(principal);
        Assert.Equal(("Pat member", UserRole.Member, user.Id),
            (cookie.FindFirstValue(MemberClaims.FullName), cookie.FindFirstValue(MemberClaims.Role), cookie.FindFirstValue(ClaimTypes.NameIdentifier)));
        await using var db = NewContext();
        Assert.NotNull((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LastLoginAt);
    }

    /// <summary>Stand-in for the portal's AdminUser: PasswordHasher&lt;T&gt; never reads the user, so the type only names it.</summary>
    private sealed class PortalAdminUser;
}
