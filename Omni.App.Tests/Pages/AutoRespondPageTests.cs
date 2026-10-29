using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Pages.Settings;
using static Omni.App.Tests.Pages.PageKit;

namespace Omni.App.Tests.Pages;

public class AutoRespondPageTests
{
    private readonly Client _client = SomeClient();
    private readonly Mock<IAutoReplyService> _autoReplies = new();
    private readonly Mock<IConfigManager> _config = new();

    public AutoRespondPageTests()
    {
        _config.SetupGet(c => c.AutoReplyEnabled).Returns(true);
        _config.SetupGet(c => c.AutoReplyMaxLength).Returns(1000);
        _autoReplies.Setup(a => a.ListAsync(_client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AutoReplyChannelDto(new AutoReplyDto(ConversationChannel.WhatsApp, true, "Hi", AutoReplyReopenedMode.Same, null, false), true, true, false),
            new AutoReplyChannelDto(new AutoReplyDto(ConversationChannel.Telegram, false, null, AutoReplyReopenedMode.Off, null, false), false, false, false),
        ]);
    }

    private AutoRespondModel Sut(Client? client) =>
        new AutoRespondModel(ClientContext(client).Object, _autoReplies.Object, _config.Object, TextKit.Localizer()).Attach();

    [Fact]
    public async Task Get_lists_the_channels_and_without_a_client_shows_nothing()
    {
        var model = Sut(_client);
        await model.OnGetAsync(default);
        Assert.True(model.ClientConfigured);
        Assert.Equal(2, model.Channels.Count);
        Assert.Equal((1000, false), (model.MaxLength, model.DisabledByAdmin));

        var none = Sut(null);
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
        Assert.Empty(none.Channels);
    }

    [Fact]
    public async Task The_first_tab_is_the_asked_one_else_the_first_connected_channel()
    {
        var model = Sut(_client);
        await model.OnGetAsync(default);
        Assert.Equal(ConversationChannel.WhatsApp, model.ActiveChannel);

        model.Tab = ConversationChannel.Telegram;
        Assert.Equal(ConversationChannel.Telegram, model.ActiveChannel);
        model.Tab = "nonsense";
        Assert.Equal(ConversationChannel.WhatsApp, model.ActiveChannel);
    }

    [Fact]
    public async Task One_save_stores_every_channel_and_comes_back_on_the_same_tab()
    {
        var model = Sut(_client);
        model.Tab = ConversationChannel.Telegram;
        model.Replies =
        [
            new AutoReplyFormRow { Channel = ConversationChannel.WhatsApp, NewConversationEnabled = true, NewConversationMessage = "Hi", ReopenedMode = AutoReplyReopenedMode.Same },
            new AutoReplyFormRow { Channel = ConversationChannel.Telegram, ReopenedMode = AutoReplyReopenedMode.Custom, ReopenedMessage = "Back", SendWhenAiAnswers = true },
        ];

        var result = await model.OnPostAsync(default);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(ConversationChannel.Telegram, redirect.RouteValues!["tab"]);
        _autoReplies.Verify(a => a.SaveAllAsync(_client.Id, It.Is<IReadOnlyList<SaveAutoReplyRequest>>(r => r.SequenceEqual(new[]
        {
            new SaveAutoReplyRequest(ConversationChannel.WhatsApp, true, "Hi", AutoReplyReopenedMode.Same, null, false),
            new SaveAutoReplyRequest(ConversationChannel.Telegram, false, null, AutoReplyReopenedMode.Custom, "Back", true),
        })), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Automatic replies saved.", model.StatusMessage);
    }

    [Fact]
    public async Task A_refused_save_opens_the_failing_channel_and_keeps_what_was_typed()
    {
        _autoReplies.Setup(a => a.SaveAllAsync(_client.Id, It.IsAny<IReadOnlyList<SaveAutoReplyRequest>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutoReplyInvalidException(new ArgumentException("autoReply.errors.reopenedMessageRequired"), ConversationChannel.Telegram));
        var model = Sut(_client);
        model.Replies =
        [
            new AutoReplyFormRow { Channel = ConversationChannel.WhatsApp, NewConversationMessage = "typed", ReopenedMode = AutoReplyReopenedMode.Off },
            new AutoReplyFormRow { Channel = ConversationChannel.Telegram, ReopenedMode = AutoReplyReopenedMode.Custom },
        ];

        var result = await model.OnPostAsync(default);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Write the message for a reopened conversation, or choose another option.", model.ErrorMessage);
        Assert.Equal(ConversationChannel.Telegram, model.ActiveChannel);
        Assert.Equal("typed", model.FormValues(model.Channels[0]).NewConversationMessage);
        Assert.Equal(AutoReplyReopenedMode.Custom, model.FormValues(model.Channels[1]).ReopenedMode);
    }

    [Fact]
    public async Task Post_without_a_client_redirects()
    {
        Assert.IsType<RedirectToPageResult>(await Sut(null).OnPostAsync(default));
        _autoReplies.Verify(a => a.SaveAllAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SaveAutoReplyRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
