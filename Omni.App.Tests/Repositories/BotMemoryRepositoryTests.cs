using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Ai;

namespace Omni.App.Tests.Repositories;

/// <summary>crm.bot_memory on Postgres: the newest window oldest first, trimming, one owner per row, and the rows go with
/// their conversation or test session (deleted, reset or swept).</summary>
public class BotMemoryRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static BotMemory Row(Guid clientId, Guid? conversationId, Guid? testSessionId, int i) => new()
    {
        Id = Guid.NewGuid(), ClientId = clientId, ConversationId = conversationId, TestSessionId = testSessionId,
        UserText = $"q{i}", Output = $"a{i}", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(i - 100)
    };

    private async Task<(Client Client, Conversation Conversation, BotTestSession Session)> SeedOwnersAsync()
    {
        var c = MakeClient("Memory");
        var contact = MakeContact(c.Id);
        var conversation = MakeConversation(c.Id, contact.Id);
        var now = DateTimeOffset.UtcNow;
        var bot = new Bot
        {
            Id = Guid.NewGuid(), ClientId = c.Id, Name = "Desk", Template = BotTemplate.Custom, Instructions = "Hi", Capabilities = [],
            KnowledgeScope = BotKnowledgeScope.All, Status = BotStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        var session = new BotTestSession { Id = Guid.NewGuid(), ClientId = c.Id, BotId = bot.Id, CreatedAt = now, UpdatedAt = now };
        await SeedAsync(c, contact, conversation, bot, session);
        return (c, conversation, session);
    }

    [PostgresFact]
    public async Task The_window_is_the_newest_exchanges_oldest_first_and_trimming_keeps_them()
    {
        var (c, conversation, session) = await SeedOwnersAsync();
        await SeedAsync(Enumerable.Range(0, 12).Select(i => (object)Row(c.Id, conversation.Id, null, i))
            .Append(Row(c.Id, null, session.Id, 50)).ToArray());

        await using var db = NewContext();
        var repo = new BotMemoryRepository(db);
        Assert.Equal(Enumerable.Range(2, 10).Select(i => $"q{i}"), (await repo.ListRecentReadOnlyAsync(conversation.Id, false, 10)).Select(m => m.UserText));
        Assert.Empty(await repo.ListRecentReadOnlyAsync(conversation.Id, false, 0));
        Assert.Equal(["q50"], (await repo.ListRecentReadOnlyAsync(session.Id, true, 10)).Select(m => m.UserText));
        Assert.Empty(await repo.ListRecentReadOnlyAsync(session.Id, false, 10)); // a key is a conversation or a test session, never mixed

        Assert.Equal(9, await repo.DeleteAllButNewestAsync(conversation.Id, false, 3));
        Assert.Equal(["q9", "q10", "q11"], (await repo.ListRecentReadOnlyAsync(conversation.Id, false, 10)).Select(m => m.UserText));
        Assert.Single(await repo.ListRecentReadOnlyAsync(session.Id, true, 10));

        repo.Add(Row(c.Id, conversation.Id, null, 99));
        await db.SaveChangesAsync();
        Assert.Equal("q99", (await repo.ListRecentReadOnlyAsync(conversation.Id, false, 1))[0].UserText);
    }

    [PostgresFact]
    public async Task A_row_has_exactly_one_owner_and_goes_with_it()
    {
        var (c, conversation, session) = await SeedOwnersAsync();
        await using (var db = NewContext())
        {
            db.BotMemory.Add(Row(c.Id, null, null, 1));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = NewContext())
        {
            db.BotMemory.Add(Row(c.Id, conversation.Id, session.Id, 1));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await SeedAsync(Row(c.Id, conversation.Id, null, 1), Row(c.Id, null, session.Id, 2));
        await using (var db = NewContext())
        {
            // Resetting the test conversation (BotTestService.ResetAsync) removes its memory.
            await new BotTestRepository(db).DeleteSessionAsync(c.Id, session.Id);
            Assert.Empty(await new BotMemoryRepository(db).ListRecentReadOnlyAsync(session.Id, true, 10));
            await db.Conversations.Where(x => x.Id == conversation.Id).ExecuteDeleteAsync();
            Assert.Empty(await new BotMemoryRepository(db).ListRecentReadOnlyAsync(conversation.Id, false, 10));
        }
    }
}
