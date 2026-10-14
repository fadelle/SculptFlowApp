using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Providers.Caching;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Helpers;
using PlasticSurgery.Persistence.Repositories.Appointments;
using PlasticSurgery.Persistence.Repositories.Clinics;
using PlasticSurgery.Persistence.Repositories.Procedures;

namespace PlasticSurgery.Tests.Repositories;

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

    private async Task RenameBehindTheCacheAsync(Guid clinicId, string name)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"update core.clinics set name = {name} where id = {clinicId}");
    }

    [Fact]
    public void Cache_lifetimes_come_from_the_settings_or_their_defaults()
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.CacheChannelsMinutes).Returns(240);
        var configured = new CacheManager(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance, config.Object);

        Assert.Equal(TimeSpan.FromHours(4), configured.LifetimeOf(CacheArea.Channels));
        Assert.Equal(TimeSpan.FromMinutes(60), _cache.LifetimeOf(CacheArea.Clinics));
        Assert.Equal(TimeSpan.FromMinutes(10), _cache.LifetimeOf(CacheArea.StaffLookup));
    }

    [PostgresFact]
    public async Task A_cached_clinic_is_served_until_a_save_changes_it()
    {
        var c = MakeClinic("Before");
        await SeedAsync(c);

        await using (var db = CachedContext())
        {
            Assert.Equal("Before", (await new ClinicRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }
        await RenameBehindTheCacheAsync(c.Id, "Behind");
        await using (var db = CachedContext())
        {
            Assert.Equal("Before", (await new ClinicRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }

        await using (var db = CachedContext())
        {
            var tracked = await new ClinicRepository(db, _cache).GetAsync(c.Id);
            tracked!.Name = "Saved";
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        Assert.Equal("Saved", (await new ClinicRepository(read, _cache).GetReadOnlyAsync(c.Id))!.Name);
    }

    [PostgresFact]
    public async Task A_save_inside_a_transaction_clears_again_on_commit_and_on_rollback()
    {
        var c = MakeClinic("Before");
        await SeedAsync(c);

        await using (var db = CachedContext())
        {
            var repo = new ClinicRepository(db, _cache);
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
            Assert.Equal("Before", (await new ClinicRepository(db, _cache).GetReadOnlyAsync(c.Id))!.Name);
        }

        await using (var db = CachedContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            (await new ClinicRepository(db, _cache).GetAsync(c.Id))!.Name = "Committed";
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        await using var read = CachedContext();
        Assert.Equal("Committed", (await new ClinicRepository(read, _cache).GetReadOnlyAsync(c.Id))!.Name);
    }

    [PostgresFact]
    public async Task Deactivating_a_membership_clears_the_signed_in_clinic_lookup()
    {
        var c = MakeClinic();
        await SeedAsync(c);
        var email = $"m-{Guid.NewGuid():N}@x.com";
        var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        await SeedAsync(user);
        var membership = new ClinicUser { Id = Guid.NewGuid(), ClinicId = c.Id, UserId = user.Id, IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        await SeedAsync(membership);

        await using (var db = CachedContext())
        {
            Assert.Equal(c.Id, (await new ClinicRepository(db, _cache).GetClinicOfActiveMemberAsync(user.Id))!.Id);
        }
        Assert.NotNull(await _cache.GetRawAsync(CacheKeys.Membership(user.Id)));

        await using (var db = CachedContext())
        {
            (await db.ClinicUsers.SingleAsync(m => m.Id == membership.Id)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        Assert.Null(await new ClinicRepository(read, _cache).GetClinicIdOfActiveMemberAsync(user.Id));
    }

    [PostgresFact]
    public async Task Procedures_and_availability_reread_after_their_rows_change()
    {
        var c = MakeClinic();
        var botox = MakeProcedure(c.Id, "Botox");
        await SeedAsync(c, botox);

        await using (var db = CachedContext())
        {
            var repo = new ProcedureRepository(db, _cache);
            Assert.Equal(["Botox"], (await repo.ListAsync(c.Id, activeOnly: true)).Select(p => p.Name));
            Assert.Null(await new AvailabilityRepository(db, _cache).GetBookingSettingsReadOnlyAsync(c.Id));
        }

        await using (var db = CachedContext())
        {
            new ProcedureRepository(db, _cache).Add(MakeProcedure(c.Id, "Abdominoplasty"));
            (await new ProcedureRepository(db, _cache).GetAsync(c.Id, botox.Id))!.IsActive = false;
            new AvailabilityRepository(db, _cache).AddBookingSettings(new ClinicBookingSettings
            {
                Id = Guid.NewGuid(), ClinicId = c.Id, DefaultConsultationDurationMinutes = 45, BufferMinutes = 5,
                MinimumBookingNoticeMinutes = 60, MaximumAdvanceBookingDays = 30, CreatedAt = Now, UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
        }

        await using var read = CachedContext();
        var procedures = new ProcedureRepository(read, _cache);
        Assert.Equal(["Abdominoplasty"], (await procedures.ListAsync(c.Id, activeOnly: true)).Select(p => p.Name));
        Assert.Equal(["Abdominoplasty", "Botox"], (await procedures.ListAsync(c.Id, activeOnly: false)).Select(p => p.Name));
        Assert.Equal(45, (await new AvailabilityRepository(read, _cache).GetBookingSettingsReadOnlyAsync(c.Id))!.DefaultConsultationDurationMinutes);
    }

    [PostgresFact]
    public async Task Deleting_a_clinic_clears_what_was_cached_about_it()
    {
        var c = MakeClinic();
        await SeedAsync(c, MakeProcedure(c.Id));
        await using var db = CachedContext();
        var clinics = new ClinicRepository(db, _cache);
        Assert.NotNull(await clinics.GetReadOnlyAsync(c.Id));
        Assert.Single(await new ProcedureRepository(db, _cache).ListAsync(c.Id, false));

        await clinics.DeleteAsync(c.Id);

        Assert.Null(await _cache.GetRawAsync(CacheKeys.Clinic(c.Id)));
        Assert.Null(await _cache.GetRawAsync(CacheKeys.Procedures(c.Id)));
        Assert.Null(await clinics.GetReadOnlyAsync(c.Id));
    }
}
