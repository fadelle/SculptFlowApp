using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Common.Extensions;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Users;

namespace Omni.App.Tests.Repositories;

/// <summary>Milestone C S2 end to end on the real Identity wiring and Postgres: an owner invites, the invitee accepts and
/// can sign in as a member of that client; open invites count as seats; the invite queries ignore case and expiry.</summary>
public class InviteFlowTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private ServiceProvider Services(List<string> links, Mock<IEntitlementService>? entitlements = null)
    {
        var emails = new Mock<IAccountEmailService>();
        emails.Setup(e => e.SendInviteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<Client>(), It.IsAny<string?>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, string?, Client, string?, string, int, CancellationToken>((_, _, _, _, _, link, _, _) => links.Add(link));
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.AccountsInviteDays).Returns(7);

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
        services.AddSingleton((entitlements ?? new Mock<IEntitlementService>()).Object);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IInviteService, InviteService>();
        services.AddAuthentication();
        services.AddOmniIdentity();
        return services.BuildServiceProvider();
    }

    private async Task<(Client Client, UserAccess Owner)> SeedOwnerAsync()
    {
        var client = MakeClient();
        var owner = MakeUser(role: UserRole.Client, fullName: "Olga");
        await SeedAsync(client, owner);
        await SeedAsync(new ClientUser { Id = Guid.NewGuid(), ClientId = client.Id, UserId = owner.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now });
        return (client, new UserAccess(owner.Id, client.Id, UserRole.Client, null, "Olga", null));
    }

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private static async Task<T> InScopeAsync<T>(IServiceProvider sp, Func<IInviteService, Task<T>> work)
    {
        using var scope = sp.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IInviteService>());
    }

    [PostgresFact]
    public async Task An_invited_person_joins_as_a_member_and_can_sign_in()
    {
        var links = new List<string>();
        await using var sp = Services(links);
        var (client, owner) = await SeedOwnerAsync();
        var email = $"new-{Guid.NewGuid():N}@x.com";

        var invite = await InScopeAsync(sp, s => s.InviteAsync(owner, new InviteMemberRequest(email, "Ann"), "http://localhost"));
        var token = TokenOf(Assert.Single(links));
        Assert.Equal(new InviteLanding(client.Name, email, "Ann"), await InScopeAsync(sp, s => s.GetLandingAsync(token)));
        Assert.Single(await InScopeAsync(sp, s => s.ListAsync(client.Id)));

        var user = await InScopeAsync(sp, s => s.AcceptAsync(token, "Ann Lee", "Passw0rd-1", "Passw0rd-1"));

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var saved = (await users.FindByEmailAsync(email))!;
            Assert.Equal((UserRole.Member, "Ann Lee", true), (saved.Role, saved.FullName, saved.EmailConfirmed));
            Assert.True(await users.CheckPasswordAsync(saved, "Passw0rd-1"));
            var access = await scope.ServiceProvider.GetRequiredService<IClientRepository>().GetUserAccessAsync(user.Id);
            Assert.Equal((client.Id, UserRole.Member), (access!.ClientId, access.Role));
        }

        await using var db = NewContext();
        var row = await db.AccountTokens.SingleAsync(t => t.Id == invite.Id);
        Assert.Equal(user.Id, row.UserId);
        Assert.NotNull(row.UsedAt);
        Assert.Empty(await InScopeAsync(sp, s => s.ListAsync(client.Id)));
        Assert.Null(await InScopeAsync(sp, s => s.GetLandingAsync(token)));
    }

    [PostgresFact]
    public async Task Open_invites_take_seats_and_a_cancelled_one_frees_it()
    {
        var links = new List<string>();
        var entitlements = new Mock<IEntitlementService>();
        // Two seats: the owner plus one more.
        entitlements.Setup(e => e.EnsureWithinLimitAsync(It.IsAny<Guid>(), EntitlementKeys.MaxUsers, It.Is<int>(n => n > 2), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxUsers, "Your plan allows 2 seats."));
        await using var sp = Services(links, entitlements);
        var (client, owner) = await SeedOwnerAsync();

        var first = await InScopeAsync(sp, s => s.InviteAsync(owner, new InviteMemberRequest($"a-{Guid.NewGuid():N}@x.com", null), "http://localhost"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() =>
            InScopeAsync(sp, s => s.InviteAsync(owner, new InviteMemberRequest($"b-{Guid.NewGuid():N}@x.com", null), "http://localhost")));

        await InScopeAsync(sp, async s => { await s.RevokeAsync(owner, first.Id); return 0; });
        await InScopeAsync(sp, s => s.InviteAsync(owner, new InviteMemberRequest($"c-{Guid.NewGuid():N}@x.com", null), "http://localhost"));
        Assert.Equal(2, links.Count);
        Assert.Null(await InScopeAsync(sp, s => s.GetLandingAsync(TokenOf(links[0]))));
    }

    [PostgresFact]
    public async Task Invite_queries_ignore_case_and_expired_or_closed_invites()
    {
        var (client, _) = await SeedOwnerAsync();
        var now = DateTimeOffset.UtcNow;
        AccountToken Invite(string email, DateTimeOffset expires, DateTimeOffset? used = null, DateTimeOffset? revoked = null) => new()
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.Invite, ClientId = client.Id, Email = email, TokenHash = AccountTokenCodec.Hash(Guid.NewGuid().ToString())!,
            ExpiresAt = expires, UsedAt = used, RevokedAt = revoked, CreatedAt = now.AddDays(-1)
        };
        var open = Invite("Ann@X.com", now.AddDays(2));
        var expired = Invite("bo@x.com", now.AddMinutes(-1));
        await SeedAsync(open, expired, Invite("cy@x.com", now.AddDays(2), used: now), Invite("di@x.com", now.AddDays(2), revoked: now));

        await using var db = NewContext();
        var repo = new AccountTokenRepository(db);
        Assert.True(await repo.HasOpenInviteAsync(client.Id, "ann@x.COM", now));
        Assert.False(await repo.HasOpenInviteAsync(client.Id, "bo@x.com", now));
        Assert.Equal(1, await repo.CountOpenInvitesAsync(client.Id, now));
        Assert.Equal(0, await repo.CountOpenInvitesAsync(client.Id, now, open.Id));
        Assert.Equal(new[] { open.Id, expired.Id }.Order(), (await repo.ListPendingInvitesAsync(client.Id)).Select(t => t.Id).Order());
        Assert.Equal(open.Id, (await repo.GetInviteAsync(client.Id, open.Id))!.Id);
        Assert.Null(await repo.GetInviteAsync(Guid.NewGuid(), open.Id));
    }
}
