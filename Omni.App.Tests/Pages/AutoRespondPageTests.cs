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
    public async Task Post_saves_one_channel_and_redirects_with_a_message()
    {
        var model = Sut(_client);
        model.Channel = ConversationChannel.Telegram;
        model.NewConversationEnabled = true;
        model.NewConversationMessage = "Hello";
        model.ReopenedMode = AutoReplyReopenedMode.Custom;
        model.ReopenedMessage = "Back";
        model.SendWhenAiAnswers = true;

        var result = await model.OnPostAsync(default);

        Assert.IsType<RedirectToPageResult>(result);
        _autoReplies.Verify(a => a.SaveAsync(_client.Id, new SaveAutoReplyRequest(ConversationChannel.Telegram, true, "Hello",
            AutoReplyReopenedMode.Custom, "Back", true), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Telegram", model.StatusMessage);
    }

    [Fact]
    public async Task A_refused_save_stays_on_the_page_and_keeps_what_was_typed_for_that_channel_only()
    {
        _autoReplies.Setup(a => a.SaveAsync(_client.Id, It.IsAny<SaveAutoReplyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("autoReply.errors.newMessageRequired"));
        var model = Sut(_client);
        model.Channel = ConversationChannel.WhatsApp;
        model.NewConversationEnabled = true;
        model.NewConversationMessage = "";

        var result = await model.OnPostAsync(default);

        Assert.IsType<PageResult>(result);
        Assert.False(string.IsNullOrEmpty(model.ErrorMessage));
        Assert.Equal("", model.FormValues(model.Channels[0]).NewConversationMessage);
        Assert.Equal(model.Channels[1].Settings, model.FormValues(model.Channels[1]));
    }

    [Fact]
    public async Task Post_without_a_client_redirects()
    {
        Assert.IsType<RedirectToPageResult>(await Sut(null).OnPostAsync(default));
        _autoReplies.Verify(a => a.SaveAsync(It.IsAny<Guid>(), It.IsAny<SaveAutoReplyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
