using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Services.Inbox;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Inbox;

namespace Omni.App.Tests.Repositories;

/// <summary>Auto-replies on Postgres: one row per client and channel (unique index, mode check), saved and read back through
/// AutoReplyService, and gone with the client.</summary>
public class AutoReplyFlowTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static AutoReplyService Sut(ApplicationDbContext db)
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.AutoReplyEnabled).Returns(true);
        config.SetupGet(c => c.AutoReplyMaxLength).Returns(1000);
        var inbox = new Mock<IInboxSettingsService>();
        inbox.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new InboxSettingsDto(false, true, true));
        var channels = new Mock<Omni.App.Persistence.Contracts.Channels.IChannelIntegrationRepository>();
        channels.Setup(c => c.ListForClientReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        return new AutoReplyService(new AutoReplyRepository(db), new ClientRepository(db), channels.Object, inbox.Object,
            [Mock.Of<IChannelSender>(s => s.Channel == ConversationChannel.WhatsApp), Mock.Of<IChannelSender>(s => s.Channel == ConversationChannel.Telegram)],
            config.Object, new UnitOfWork(db));
    }

    [PostgresFact]
    public async Task Settings_are_saved_per_channel_updated_in_place_and_read_back()
    {
        var client = MakeClient("Glow");
        await SeedAsync(client);

        await using (var db = NewContext())
        {
            await Sut(db).SaveAsync(client.Id, new SaveAutoReplyRequest(ConversationChannel.WhatsApp, true, "Hi!", AutoReplyReopenedMode.Same, null, false));
            await Sut(db).SaveAsync(client.Id, new SaveAutoReplyRequest(ConversationChannel.Telegram, false, null, AutoReplyReopenedMode.Custom, "Back?", true));
        }
        await using (var db = NewContext())
        {
            await Sut(db).SaveAsync(client.Id, new SaveAutoReplyRequest(ConversationChannel.WhatsApp, true, "Hello!", AutoReplyReopenedMode.Off, null, false));
        }

        await using (var db = NewContext())
        {
            Assert.Equal(2, await db.AutoReplies.CountAsync(a => a.ClientId == client.Id));
            var list = await Sut(db).ListAsync(client.Id);
            Assert.Equal(new AutoReplyDto(ConversationChannel.WhatsApp, true, "Hello!", AutoReplyReopenedMode.Off, null, false), list[0].Settings);
            Assert.Equal(new AutoReplyDto(ConversationChannel.Telegram, false, null, AutoReplyReopenedMode.Custom, "Back?", true), list[1].Settings);
            Assert.Equal(AutoReplyChoice.Send("Back?"),
                await Sut(db).ChooseAsync(client.Id, ConversationChannel.Telegram, AutoReplyTrigger.Reopened, aiAnswers: true));
        }
    }

    [PostgresFact]
    public async Task The_database_refuses_a_second_row_per_channel_and_an_unknown_mode_and_drops_rows_with_the_client()
    {
        var client = MakeClient("Glow");
        await SeedAsync(client, new AutoReply { Id = Guid.NewGuid(), ClientId = client.Id, Channel = ConversationChannel.WhatsApp });

        await using (var db = NewContext())
        {
            db.AutoReplies.Add(new AutoReply { Id = Guid.NewGuid(), ClientId = client.Id, Channel = ConversationChannel.WhatsApp });
            await Assert.ThrowsAsync<DuplicateRecordException>(() => new UnitOfWork(db).SaveChangesAsync());
        }
        await using (var db = NewContext())
        {
            db.AutoReplies.Add(new AutoReply { Id = Guid.NewGuid(), ClientId = client.Id, Channel = ConversationChannel.Telegram, ReopenedMode = "sometimes" });
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        }
        await using (var db = NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("delete from core.clients where id = {0}", client.Id);
            Assert.False(await db.AutoReplies.AnyAsync(a => a.ClientId == client.Id));
        }
    }
}
