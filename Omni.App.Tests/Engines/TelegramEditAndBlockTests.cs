using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Engines.Telegram;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Engines.Telegram;

namespace Omni.App.Tests.Engines;

/// <summary>QA t3-inbox-17: Telegram edits and "blocked the bot" were dropped silently.</summary>
public class TelegramEditAndBlockTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IMessageRepository> _messageRows = new();
    private readonly Mock<IContactRepository> _contactRows = new();
    private readonly Mock<IOptOutService> _optOut = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMessageService> _messages = new();

    private TelegramWebhookProcessor Sut() => new(Mock.Of<IChannelIntegrationRepository>(), Mock.Of<IAiTriggerNotifier>(), Mock.Of<IContactService>(),
        Mock.Of<IConversationService>(), _messages.Object, NullLogger<TelegramWebhookProcessor>.Instance, Mock.Of<IMessageTracer>(),
        _messageRows.Object, _contactRows.Object, _optOut.Object, _uow.Object);

    private ChannelIntegration Connection() => new() { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.Telegram };

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task An_edit_updates_the_stored_message_and_never_runs_the_ai()
    {
        var stored = new Message { Id = Guid.NewGuid(), ClientId = _clientId, Direction = MessageDirection.Inbound, Content = "helo" };
        _messageRows.Setup(m => m.FindByExternalIdAsync(_clientId, ConversationChannel.Telegram, "5:42", It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var result = await Sut().ProcessAsync(Connection(), J("{\"update_id\":7,\"edited_message\":{\"message_id\":42,\"date\":1790000000,\"chat\":{\"id\":5,\"type\":\"private\"},\"from\":{\"id\":5,\"is_bot\":false},\"text\":\"hello\"}}"));

        Assert.Equal(("edited_message", true, false), (result.EventType, result.Processed, result.ShouldRunAi));
        Assert.Equal("hello", stored.Content);
        Assert.Contains("\"editedAt\"", stored.MetadataJson);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _messages.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_edit_of_a_message_omni_never_stored_is_ignored()
    {
        var result = await Sut().ProcessAsync(Connection(), J("{\"update_id\":7,\"edited_message\":{\"message_id\":1,\"chat\":{\"id\":5,\"type\":\"private\"},\"text\":\"x\"}}"));
        Assert.False(result.Processed);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("kicked", true)]
    [InlineData("member", false)]
    public async Task Blocking_opts_the_contact_out_and_restarting_opts_them_back_in(string status, bool optedOut)
    {
        var contact = new Contact { Id = Guid.NewGuid(), ClientId = _clientId };
        _contactRows.Setup(c => c.FindByExternalIdAsync(_clientId, "telegram:9", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        _optOut.Setup(o => o.Apply(contact, optedOut, It.IsAny<string>(), null)).Returns(true);

        var result = await Sut().ProcessAsync(Connection(), J("{\"update_id\":8,\"my_chat_member\":{\"chat\":{\"id\":9,\"type\":\"private\"},\"new_chat_member\":{\"status\":\"" + status + "\"}}}"));

        Assert.Equal(("member_update", true), (result.EventType, result.Processed));
        _optOut.Verify(o => o.Apply(contact, optedOut, optedOut ? "telegram_blocked" : "telegram_unblocked", null), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
