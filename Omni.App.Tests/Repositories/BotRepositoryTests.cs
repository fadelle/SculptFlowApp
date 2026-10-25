using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Ai;

namespace Omni.App.Tests.Repositories;

/// <summary>AI bots and their test conversations on Postgres: capabilities as text[], knowledge sources (one kind each, gone with
/// their document), test sessions locked for a handoff and swept when stale.</summary>
public class BotRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task A_bot_round_trips_with_its_capabilities_and_sources_and_is_scoped_to_its_client()
    {
        var c = MakeClient("Bots");
        var other = MakeClient("Other");
        var now = DateTimeOffset.UtcNow;
        var document = new KnowledgeDocument { Id = Guid.NewGuid(), ClientId = c.Id, Title = "Prices", Content = "…", CreatedAt = now, UpdatedAt = now };
        await SeedAsync(c, other, document);
        var bot = new Bot
        {
            Id = Guid.NewGuid(), ClientId = c.Id, Name = "Front desk", Template = BotTemplate.Custom, Instructions = "Be brief.",
            Capabilities = [BotCapability.Knowledge, BotCapability.Pipeline], KnowledgeScope = BotKnowledgeScope.Selected, Language = "fr",
            Status = BotStatus.Active, CreatedAt = now, UpdatedAt = now,
            KnowledgeSources = [new BotKnowledgeSource { Id = Guid.NewGuid(), KnowledgeDocumentId = document.Id }]
        };
        await using (var db = NewContext())
        {
            new BotRepository(db).Add(bot);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var repo = new BotRepository(db);
            var read = Assert.Single(await repo.ListForClientReadOnlyAsync(c.Id));
            Assert.Equal([BotCapability.Knowledge, BotCapability.Pipeline], read.Capabilities);
            Assert.Equal(document.Id, Assert.Single(read.KnowledgeSources).KnowledgeDocumentId);
            Assert.Null(await repo.GetAsync(other.Id, bot.Id));
            Assert.Empty(await repo.ListForClientReadOnlyAsync(other.Id));
        }

        // A source row must name exactly one kind of source.
        await using (var db = NewContext())
        {
            db.BotKnowledgeSources.Add(new BotKnowledgeSource { Id = Guid.NewGuid(), BotId = bot.Id });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Deleting the document drops it from the bot.
        await using (var db = NewContext())
        {
            await db.KnowledgeDocuments.Where(d => d.Id == document.Id).ExecuteDeleteAsync();
            var repo = new BotRepository(db);
            Assert.Empty((await repo.GetAsync(c.Id, bot.Id))!.KnowledgeSources);
            repo.Remove((await repo.GetAsync(c.Id, bot.Id))!);
            await db.SaveChangesAsync();
            Assert.Empty(await repo.ListForClientReadOnlyAsync(c.Id));
        }
    }

    [PostgresFact]
    public async Task Test_sessions_are_locked_for_a_handoff_listed_in_order_and_swept_when_stale()
    {
        var c = MakeClient("Bot tests");
        var now = DateTimeOffset.UtcNow;
        var bot = new Bot { Id = Guid.NewGuid(), ClientId = c.Id, Name = "B", Template = BotTemplate.Support, CreatedAt = now, UpdatedAt = now };
        await SeedAsync(c, bot);
        var fresh = new BotTestSession { Id = Guid.NewGuid(), ClientId = c.Id, BotId = bot.Id, CreatedAt = now, UpdatedAt = now };
        var stale = new BotTestSession { Id = Guid.NewGuid(), ClientId = c.Id, BotId = bot.Id, CreatedAt = now.AddDays(-3), UpdatedAt = now.AddDays(-3) };
        await using (var db = NewContext())
        {
            var repo = new BotTestRepository(db);
            repo.AddSession(fresh);
            repo.AddSession(stale);
            repo.AddMessage(new BotTestMessage { Id = Guid.NewGuid(), SessionId = fresh.Id, ClientId = c.Id, Role = BotTestRole.Bot, Content = "Hello", CreatedAt = now.AddSeconds(1) });
            repo.AddMessage(new BotTestMessage { Id = Guid.NewGuid(), SessionId = fresh.Id, ClientId = c.Id, Role = BotTestRole.Contact, Content = "Hi", CreatedAt = now });
            await db.SaveChangesAsync();
            // The database refuses an unknown role.
            repo.AddMessage(new BotTestMessage { Id = Guid.NewGuid(), SessionId = fresh.Id, ClientId = c.Id, Role = "robot", Content = "x", CreatedAt = now });
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = NewContext())
        {
            var repo = new BotTestRepository(db);
            Assert.Equal(["Hi", "Hello"], (await repo.ListMessagesAsync(fresh.Id)).Select(m => m.Content));
            await using var tx = await new UnitOfWork(db).BeginTransactionAsync();
            var locked = await repo.GetSessionForUpdateAsync(c.Id, fresh.Id);
            Assert.NotNull(locked);
            locked!.HandedOff = true;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Assert.Null(await repo.GetSessionForUpdateAsync(Guid.NewGuid(), fresh.Id));
        }

        await using (var db = NewContext())
        {
            var repo = new BotTestRepository(db);
            Assert.True((await repo.GetSessionAsync(c.Id, fresh.Id))!.HandedOff);
            Assert.True(await repo.DeleteSessionsOlderThanAsync(now.AddDays(-1)) >= 1);
            Assert.Null(await repo.GetSessionAsync(c.Id, stale.Id));
            Assert.Equal(0, await repo.DeleteSessionAsync(Guid.NewGuid(), fresh.Id)); // another client's id deletes nothing
            Assert.Equal(1, await repo.DeleteSessionAsync(c.Id, fresh.Id));
            Assert.Equal(0, await db.BotTestMessages.CountAsync(m => m.SessionId == fresh.Id)); // lines go with their session
        }
    }
}
