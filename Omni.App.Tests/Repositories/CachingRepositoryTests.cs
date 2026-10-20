using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.Clients;
using Omni.App.Business.Services.Inbox;
using Omni.App.Persistence.Repositories.Inbox;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Helpers;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Bookings;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Catalog;

namespace Omni.App.Tests.Repositories;

/// <summary>
/// The cached "ReadOnly" repository reads against the throwaway Postgres: a second read is answered from the cache, and
/// CacheInvalidationInterceptor clears the key when a save (or its transaction) changes the row. Rows are changed behind
/// the cache with raw SQL to prove a read came from the cache.
/// </summary>
public class CachingRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);
    private readonly PostgresFixture _fixture = fixture;

    private ApplicationDbContext CachedContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_fixture.ConnectionString)
            .AddInterceptors(new CacheInvalidationInterceptor(_cache)).Options);

    private async Task RenameBehindTheCacheAsync(Guid clientId, string name)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"update core.clients set name = {name} where id = {clientId}");
    }

    [Fact]
    public void Cache_lifetimes_come_from_the_settings_or_their_defaults()
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.CacheChannelsMinutes).Returns(240);
        var configured = new CacheManager(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance, config.Object);

        Assert.Equal(TimeSpan.FromHours(4), configured.LifetimeOf(CacheArea.Channels));
        Assert.Equal(TimeSpan.FromMinutes(60), _cache.LifetimeOf(CacheArea.Clients));
        Assert.Equal(TimeSpan.FromMinutes(10), _cache.LifetimeOf(CacheArea.Memberships));
    }

    [PostgresFact]
    public async Task A_cached_client_is_served_until_a_save_changes_it()
    {
        var c = MakeClient("Before");
        await SeedAsync(c);

        await using (var db = CachedContext())
        {
            Assert.Equal("Before", (await new ClientRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }
        await RenameBehindTheCacheAsync(c.Id, "Behind");
        await using (var db = CachedContext())
        {
            Assert.Equal("Before", (await new ClientRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }

        await using (var db = CachedContext())
        {
            var tracked = await new ClientRepository(db, _cache).GetAsync(c.Id);
            tracked!.Name = "Saved";
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        Assert.Equal("Saved", (await new ClientRepository(read, _cache).GetReadOnlyAsync(c.Id))!.Name);
    }

    [PostgresFact]
    public async Task A_new_default_language_reaches_the_next_cached_client_read()
    {
        // Business info and the platform-admin endpoint (ClientAdminService) both save through ClientProfileService.
        var c = MakeClient("Language");
        await SeedAsync(c);
        await using (var db = CachedContext())
        {
            Assert.Equal("en", (await new ClientRepository(db, _cache).GetReadOnlyAsync(c.Id))!.DefaultLanguage);
        }

        await using (var db = CachedContext())
        {
            await new ClientProfileService(new ClientRepository(db, _cache), new UnitOfWork(db)).SetDefaultLanguageAsync(c.Id, "ar");
        }

        await using var read = CachedContext();
        Assert.Equal("ar", (await new ClientRepository(read, _cache).GetReadOnlyAsync(c.Id))!.DefaultLanguage);
    }

    [PostgresFact]
    public async Task Inbox_settings_default_to_the_bot_on_and_each_save_reaches_the_next_cached_read()
    {
        var c = MakeClient("Inbox");
        await SeedAsync(c);
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.GetAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        InboxSettingsService Service(ApplicationDbContext db) => new(new InboxSettingsRepository(db, _cache), new ClientRepository(db, _cache),
            entitlements.Object, new Omni.App.Persistence.Repositories.UnitOfWork(db));

        // No row yet: the defaults (an existing client keeps the bot on), and "no row" is cached.
        await using (var db = CachedContext())
        {
            Assert.Equal(new InboxSettingsDto(true, true, true), await Service(db).GetAsync(c.Id));
        }

        // The first save creates the client-wide row and clears the cached "no row".
        await using (var db = CachedContext())
        {
            await Service(db).UpdateAsync(c.Id, new UpdateInboxSettingsRequest(false, true));
        }
        await using (var db = CachedContext())
        {
            var after = await Service(db).GetAsync(c.Id);
            Assert.Equal((false, true, ConversationMode.Human),
                (after.AiBotNewConversations, after.AiBotReopenedConversations, await Service(db).InitialModeAsync(c.Id)));
        }

        // A later save updates the same row.
        await using (var db = CachedContext())
        {
            await Service(db).UpdateAsync(c.Id, new UpdateInboxSettingsRequest(true, false));
        }
        await using (var db = CachedContext())
        {
            Assert.Equal(new InboxSettingsDto(true, true, false), await Service(db).GetAsync(c.Id));
            Assert.Equal(1, await db.InboxSettings.CountAsync(s => s.ClientId == c.Id));
        }

        // Only one client-wide row per client.
        await using (var db = NewContext())
        {
            db.InboxSettings.Add(new InboxSettings { Id = Guid.NewGuid(), ClientId = c.Id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DuplicateRecordException>(() => new Omni.App.Persistence.Repositories.UnitOfWork(db).SaveChangesAsync());
        }
    }

    [PostgresFact]
    public async Task A_save_inside_a_transaction_clears_again_on_commit_and_on_rollback()
    {
        var c = MakeClient("Before");
        await SeedAsync(c);

        await using (var db = CachedContext())
        {
            var repo = new ClientRepository(db, _cache);
            await using var tx = await db.Database.BeginTransactionAsync();
            (await repo.GetAsync(c.Id))!.Name = "Rolled back";
            await db.SaveChangesAsync();
            // This request reads its own uncommitted row and caches it...
            Assert.Equal("Rolled back", (await repo.GetReadOnlyAsync(c.Id))!.Name);
            await tx.RollbackAsync();
        }

        // ...but the rollback clears it again, so nobody is served a row that never existed.
        await using (var db = CachedContext())
        {
            Assert.Equal("Before", (await new ClientRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }

        await using (var db = CachedContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            (await new ClientRepository(db, _cache).GetAsync(c.Id))!.Name = "Committed";
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using var read = CachedContext();
        Assert.Equal("Committed", (await new ClientRepository(read, _cache).GetReadOnlyAsync(c.Id))!.Name);
    }

    [PostgresFact]
    public async Task Deactivating_a_membership_clears_the_signed_in_client_lookup()
    {
        var c = MakeClient();
        await SeedAsync(c);
        var email = $"m-{Guid.NewGuid():N}@x.com";
        var user = new AppUser { Role = UserRole.Client, Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        await SeedAsync(user);
        var membership = new ClientUser { Id = Guid.NewGuid(), ClientId = c.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(membership);

        await using (var db = CachedContext())
        {
            Assert.Equal(c.Id, (await new ClientRepository(db, _cache).GetUserAccessAsync(user.Id))!.ClientId);
        }
        Assert.NotNull(await _cache.GetRawAsync(CacheKeys.Membership(user.Id)));

        await using (var db = CachedContext())
        {
            (await db.ClientUsers.SingleAsync(m => m.Id == membership.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        Assert.Null(await new ClientRepository(read, _cache).GetUserAccessAsync(user.Id));
    }

    [PostgresFact]
    public async Task Deactivating_a_user_or_changing_their_stamp_clears_the_signed_in_access()
    {
        var c = MakeClient();
        var user = MakeUser(role: UserRole.Member);
        await SeedAsync(c, user);
        await SeedAsync(new ClientUser { Id = Guid.NewGuid(), ClientId = c.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now });

        await using (var db = CachedContext())
            Assert.Equal(user.SecurityStamp, (await new ClientRepository(db, _cache).GetUserAccessAsync(user.Id))!.SecurityStamp);

        await using (var db = CachedContext())
        {
            (await db.Users.SingleAsync(u => u.Id == user.Id)).SecurityStamp = "renewed";
            await db.SaveChangesAsync();
        }
        await using (var db = CachedContext())
            Assert.Equal("renewed", (await new ClientRepository(db, _cache).GetUserAccessAsync(user.Id))!.SecurityStamp);

        await using (var db = CachedContext())
        {
            (await db.Users.SingleAsync(u => u.Id == user.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await using var read = CachedContext();
        Assert.Null(await new ClientRepository(read, _cache).GetUserAccessAsync(user.Id));
    }

    [PostgresFact]
    public async Task Services_and_availability_reread_after_their_rows_change()
    {
        var c = MakeClient();
        var botox = MakeService(c.Id, "Botox");
        await SeedAsync(c, botox);

        await using (var db = CachedContext())
        {
            var repo = new ServiceRepository(db, _cache);
            Assert.Equal(["Botox"], (await repo.ListAsync(c.Id, activeOnly: true)).Select(p => p.Name));
            Assert.Null(await new AvailabilityRepository(db, _cache).GetBookingSettingsReadOnlyAsync(c.Id));
        }

        await using (var db = CachedContext())
        {
            new ServiceRepository(db, _cache).Add(MakeService(c.Id, "Abdominoplasty"));
            (await new ServiceRepository(db, _cache).GetAsync(c.Id, botox.Id))!.IsActive = false;
            new AvailabilityRepository(db, _cache).AddBookingSettings(new BookingSettings
            {
                Id = Guid.NewGuid(), ClientId = c.Id, DefaultBookingDurationMinutes = 45, BufferMinutes = 5,
                MinimumBookingNoticeMinutes = 60, MaximumAdvanceBookingDays = 30, CreatedAt = Now, UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        var services = new ServiceRepository(read, _cache);
        Assert.Equal(["Abdominoplasty"], (await services.ListAsync(c.Id, activeOnly: true)).Select(p => p.Name));
        Assert.Equal(["Abdominoplasty", "Botox"], (await services.ListAsync(c.Id, activeOnly: false)).Select(p => p.Name));
        Assert.Equal(45, (await new AvailabilityRepository(read, _cache).GetBookingSettingsReadOnlyAsync(c.Id))!.DefaultBookingDurationMinutes);
    }

    [PostgresFact]
    public async Task Deleting_a_client_clears_what_was_cached_about_it()
    {
        var c = MakeClient();
        await SeedAsync(c, MakeService(c.Id));
        await using var db = CachedContext();
        var clients = new ClientRepository(db, _cache);
        Assert.NotNull(await clients.GetReadOnlyAsync(c.Id));
        Assert.Single(await new ServiceRepository(db, _cache).ListAsync(c.Id, false));

        await clients.DeleteAsync(c.Id);

        Assert.Null(await _cache.GetRawAsync(CacheKeys.Client(c.Id)));
        Assert.Null(await _cache.GetRawAsync(CacheKeys.Services(c.Id)));
        Assert.Null(await clients.GetReadOnlyAsync(c.Id));
    }
}
