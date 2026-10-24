using Omni.App.Business.Services.Inbox;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Tests.Services;

public class AutoReplyServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IAutoReplyRepository> _autoReplies = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IChannelIntegrationRepository> _channels = new();
    private readonly Mock<IInboxSettingsService> _inboxSettings = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<AutoReply> _added = [];

    public AutoReplyServiceTests()
    {
        _clients.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow" });
        _channels.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new ChannelIntegration { ClientId = _clientId, Channel = ConversationChannel.WhatsApp, Status = ChannelIntegrationStatus.Connected }]);
        _autoReplies.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _autoReplies.Setup(r => r.Add(It.IsAny<AutoReply>())).Callback<AutoReply>(_added.Add);
        Inbox(new InboxSettingsDto(true, true, false));
        _config.SetupGet(c => c.AutoReplyEnabled).Returns(true);
        _config.SetupGet(c => c.AutoReplyMaxLength).Returns(20);
    }

    private void Inbox(InboxSettingsDto settings) =>
        _inboxSettings.Setup(s => s.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

    private static IChannelSender Sender(string channel) => Mock.Of<IChannelSender>(s => s.Channel == channel);

    private AutoReplyService Sut() => new(_autoReplies.Object, _clients.Object, _channels.Object, _inboxSettings.Object,
        [Sender(ConversationChannel.WhatsApp), Sender(ConversationChannel.Telegram)], _config.Object, _uow.Object);

    private static SaveAutoReplyRequest Request(bool newOn = true, string? newMessage = "Hi!", string mode = AutoReplyReopenedMode.Off,
        string? reopenedMessage = null, string channel = ConversationChannel.WhatsApp) =>
        new(channel, newOn, newMessage, mode, reopenedMessage, false);

    [Fact]
    public async Task Every_sendable_channel_is_listed_with_the_defaults_connection_and_bot_state()
    {
        var list = await Sut().ListAsync(_clientId);

        Assert.Equal([ConversationChannel.WhatsApp, ConversationChannel.Telegram], list.Select(c => c.Settings.Channel));
        var whatsApp = list[0];
        Assert.Equal(new AutoReplyDto(ConversationChannel.WhatsApp, false, null, AutoReplyReopenedMode.Off, null, false), whatsApp.Settings);
        Assert.Equal((true, true, false), (whatsApp.Connected, whatsApp.AiBotAnswersNew, whatsApp.AiBotAnswersReopened));
        Assert.False(list[1].Connected);
    }

    [Fact]
    public async Task A_channel_beyond_the_ai_bot_limit_is_listed_as_answered_by_the_team()
    {
        Inbox(new InboxSettingsDto(true, true, true, new HashSet<string> { ConversationChannel.Telegram }, 1));
        _autoReplies.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new AutoReply { ClientId = _clientId, Channel = ConversationChannel.Telegram, NewConversationEnabled = true, NewConversationMessage = "Yo" }]);

        var telegram = (await Sut().ListAsync(_clientId))[1];

        Assert.Equal((false, false, true, "Yo"),
            (telegram.AiBotAnswersNew, telegram.AiBotAnswersReopened, telegram.Settings.NewConversationEnabled, telegram.Settings.NewConversationMessage));
    }

    [Fact]
    public async Task Saving_creates_the_row_once_and_trims_the_messages()
    {
        await Sut().SaveAsync(_clientId, Request(newMessage: "  Hi!  ", mode: AutoReplyReopenedMode.Custom, reopenedMessage: " Back? "));

        var row = Assert.Single(_added);
        Assert.Equal((_clientId, ConversationChannel.WhatsApp, true, "Hi!", AutoReplyReopenedMode.Custom, "Back?"),
            (row.ClientId, row.Channel, row.NewConversationEnabled, row.NewConversationMessage, row.ReopenedMode, row.ReopenedMessage));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Saving_updates_an_existing_row_and_keeps_text_of_a_reply_that_is_off()
    {
        var row = new AutoReply { Id = Guid.NewGuid(), ClientId = _clientId, Channel = ConversationChannel.WhatsApp, NewConversationEnabled = true };
        _autoReplies.Setup(r => r.GetAsync(_clientId, ConversationChannel.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        await Sut().SaveAsync(_clientId, Request(newOn: false, newMessage: "Kept", mode: AutoReplyReopenedMode.Same));

        Assert.Empty(_added);
        Assert.Equal((false, "Kept", AutoReplyReopenedMode.Same), (row.NewConversationEnabled, row.NewConversationMessage, row.ReopenedMode));
    }

    public static TheoryData<SaveAutoReplyRequest, string> Invalid => new()
    {
        { Request(channel: ConversationChannel.Sms), "autoReply.errors.channel" },
        { Request(mode: "sometimes"), "autoReply.errors.mode" },
        { Request(newMessage: "  "), "autoReply.errors.newMessageRequired" },
        { Request(newOn: false, newMessage: null, mode: AutoReplyReopenedMode.Same), "autoReply.errors.sameNeedsNewMessage" },
        { Request(mode: AutoReplyReopenedMode.Custom, reopenedMessage: ""), "autoReply.errors.reopenedMessageRequired" },
        { Request(newMessage: new string('a', 21)), "autoReply.errors.tooLong" },
        { Request(mode: AutoReplyReopenedMode.Custom, reopenedMessage: new string('a', 21)), "autoReply.errors.tooLong" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_settings_are_refused_before_anything_is_saved(SaveAutoReplyRequest request, string key)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(_clientId, request));

        Assert.StartsWith(key, ex.Message);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_client_is_not_found()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ListAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SaveAsync(Guid.NewGuid(), Request()));
    }

    [Fact]
    public async Task Choosing_reads_the_channel_row_and_the_master_switch()
    {
        _autoReplies.Setup(r => r.GetReadOnlyAsync(_clientId, ConversationChannel.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(
            new AutoReply { ClientId = _clientId, Channel = ConversationChannel.WhatsApp, NewConversationEnabled = true, NewConversationMessage = "Hi!" });

        Assert.Equal(AutoReplyChoice.Send("Hi!"), await Sut().ChooseAsync(_clientId, ConversationChannel.WhatsApp, AutoReplyTrigger.NewConversation, false));
        Assert.Equal(AutoReplyChoice.Skip(AutoReplyRules.SkipOff), await Sut().ChooseAsync(_clientId, ConversationChannel.Telegram, AutoReplyTrigger.NewConversation, false));

        _config.SetupGet(c => c.AutoReplyEnabled).Returns(false);
        Assert.Equal(AutoReplyChoice.Skip(AutoReplyRules.SkipDisabledByAdmin), await Sut().ChooseAsync(_clientId, ConversationChannel.WhatsApp, AutoReplyTrigger.NewConversation, false));
    }
}
