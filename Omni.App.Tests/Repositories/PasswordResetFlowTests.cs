using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Business.Services.Accounts;
using Omni.App.Common.Extensions;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Users;

namespace Omni.App.Tests.Repositories;

/// <summary>Milestone C S1 end to end on the real Identity wiring and Postgres: a reset link changes the password once,
/// renews the security stamp (other sessions sign out), clears a lockout and stops working afterwards; the token store
/// keeps only hashes.</summary>
public class PasswordResetFlowTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private const string OldPassword = "Old-passw0rd";

    private ServiceProvider Services(List<string> links)
    {
        var emails = new Mock<IAccountEmailService>();
        emails.Setup(e => e.SendPasswordResetAsync(It.IsAny<AppUser>(), It.IsAny<Client?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<AppUser, Client?, string?, string, int, CancellationToken>((_, _, _, link, _, _) => links.Add(link));
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.AccountsResetLinkMinutes).Returns(60);
        config.SetupGet(c => c.AccountsResetEmailsPerHour).Returns(2);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IAccountTokenRepository, AccountTokenRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(_ => TextKit.Localizer());
        services.AddSingleton(emails.Object);
        services.AddSingleton(config.Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddAuthentication();
        services.AddOmniIdentity();
        return services.BuildServiceProvider();
    }

    private async Task<AppUser> CreateMemberAsync(IServiceProvider sp)
    {
        var client = MakeClient();
        await SeedAsync(client);
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"reset-{Guid.NewGuid():N}@x.com";
        var user = new AppUser { UserName = email, Email = email, Role = UserRole.Member, FullName = "Pat", IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        Assert.True((await users.CreateAsync(user, OldPassword)).Succeeded);
        await SeedAsync(new ClientUser { Id = Guid.NewGuid(), ClientId = client.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now });
        return user;
    }

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private static async Task<T> InScopeAsync<T>(IServiceProvider sp, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = sp.CreateScope();
        return await work(scope.ServiceProvider);
    }

    [PostgresFact]
    public async Task A_reset_link_changes_the_password_once_and_signs_other_sessions_out()
    {
        var links = new List<string>();
        await using var sp = Services(links);
        var user = await CreateMemberAsync(sp);
        var stampBefore = user.SecurityStamp;

        // Lock the account first: the reset clears it.
        await InScopeAsync(sp, async s =>
        {
            var users = s.GetRequiredService<UserManager<AppUser>>();
            return await users.SetLockoutEndDateAsync((await users.FindByIdAsync(user.Id))!, DateTimeOffset.UtcNow.AddHours(1));
        });

        await InScopeAsync(sp, async s => { await s.GetRequiredService<IPasswordResetService>().RequestAsync(user.Email!.ToUpperInvariant(), "http://localhost", null); return 0; });
        var token = TokenOf(Assert.Single(links));

        await using (var db = NewContext())
        {
            var stored = await db.AccountTokens.SingleAsync(t => t.UserId == user.Id);
            Assert.Equal(AccountTokenCodec.Hash(token), stored.TokenHash);
            Assert.DoesNotContain(token, stored.TokenHash);
        }

        Assert.True(await InScopeAsync(sp, s => s.GetRequiredService<IPasswordResetService>().IsUsableAsync(token)));
        await InScopeAsync(sp, async s => { await s.GetRequiredService<IPasswordResetService>().ResetAsync(token, "N3w-passw0rd", "N3w-passw0rd"); return 0; });

        var after = await InScopeAsync(sp, async s => (await s.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(user.Id))!);
        Assert.NotEqual(stampBefore, after.SecurityStamp);
        Assert.Null(after.LockoutEnd);
        Assert.True(after.EmailConfirmed);
        Assert.True(await InScopeAsync(sp, s => s.GetRequiredService<UserManager<AppUser>>().CheckPasswordAsync(after, "N3w-passw0rd")));
        Assert.False(await InScopeAsync(sp, s => s.GetRequiredService<UserManager<AppUser>>().CheckPasswordAsync(after, OldPassword)));

        // Used once: the same link no longer works.
        Assert.False(await InScopeAsync(sp, s => s.GetRequiredService<IPasswordResetService>().IsUsableAsync(token)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InScopeAsync(sp, async s => { await s.GetRequiredService<IPasswordResetService>().ResetAsync(token, "An0ther-pw", "An0ther-pw"); return 0; }));
    }

    [PostgresFact]
    public async Task Only_the_hourly_number_of_links_is_sent_and_a_reset_closes_the_older_ones()
    {
        var links = new List<string>();
        await using var sp = Services(links);
        var user = await CreateMemberAsync(sp);

        for (var i = 0; i < 3; i++)
            await InScopeAsync(sp, async s => { await s.GetRequiredService<IPasswordResetService>().RequestAsync(user.Email, "http://localhost", null); return 0; });
        Assert.Equal(2, links.Count);

        await InScopeAsync(sp, async s => { await s.GetRequiredService<IPasswordResetService>().ResetAsync(TokenOf(links[1]), "N3w-passw0rd", "N3w-passw0rd"); return 0; });

        Assert.False(await InScopeAsync(sp, s => s.GetRequiredService<IPasswordResetService>().IsUsableAsync(TokenOf(links[0]))));
        await using var db = NewContext();
        var rows = await db.AccountTokens.Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.UsedAt is not null);
        Assert.Single(rows, r => r.RevokedAt is not null);
    }

    [PostgresFact]
    public async Task The_token_repository_counts_and_lists_by_user_purpose_and_time()
    {
        var user = MakeUser();
        var client = MakeClient();
        await SeedAsync(user, client);
        var now = DateTimeOffset.UtcNow;
        AccountToken Token(DateTimeOffset created, DateTimeOffset expires, DateTimeOffset? used = null, string purpose = AccountTokenPurpose.PasswordReset) => new()
        {
            Id = Guid.NewGuid(), Purpose = purpose, UserId = user.Id, ClientId = client.Id, Email = user.Email!, TokenHash = AccountTokenCodec.Hash(Guid.NewGuid().ToString())!,
            ExpiresAt = expires, UsedAt = used, CreatedAt = created
        };
        var open = Token(now.AddMinutes(-5), now.AddMinutes(55));
        await SeedAsync(open, Token(now.AddHours(-2), now.AddHours(-1)), Token(now.AddMinutes(-10), now.AddMinutes(50), used: now),
            Token(now.AddMinutes(-1), now.AddDays(7), purpose: AccountTokenPurpose.Invite));

        await using var db = NewContext();
        var repo = new AccountTokenRepository(db);
        Assert.Equal(2, await repo.CountForUserSinceAsync(AccountTokenPurpose.PasswordReset, user.Id, now.AddHours(-1)));
        Assert.Equal(open.Id, Assert.Single(await repo.ListOpenForUserAsync(AccountTokenPurpose.PasswordReset, user.Id, now)).Id);
        Assert.Equal(open.Id, (await repo.GetByHashAsync(open.TokenHash))!.Id);
        Assert.Null(await repo.GetByHashAsync(new string('0', 64)));
    }
}
