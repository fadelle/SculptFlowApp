namespace Omni.App.Tests.Helpers;

public class AutoReplyRulesTests
{
    private static AutoReplyDto Settings(bool newOn = true, string? newMessage = "Hi", string mode = AutoReplyReopenedMode.Off,
        string? reopened = null, bool withAi = false) =>
        new(ConversationChannel.WhatsApp, newOn, newMessage, mode, reopened, withAi);

    [Theory]
    [InlineData(true, "Hi", AutoReplyTrigger.NewConversation, "Hi")]
    [InlineData(false, "Hi", AutoReplyTrigger.NewConversation, null)]
    [InlineData(true, "  ", AutoReplyTrigger.NewConversation, null)]
    [InlineData(true, " Hi ", AutoReplyTrigger.NewConversation, "Hi")]
    public void The_new_conversation_reply_needs_its_switch_and_a_message(bool newOn, string message, string trigger, string? expected) =>
        Assert.Equal(expected, AutoReplyRules.Choose(Settings(newOn, message), trigger, aiAnswers: false).Text);

    [Theory]
    [InlineData(AutoReplyReopenedMode.Off, null)]
    [InlineData(AutoReplyReopenedMode.Same, "Hi")]
    [InlineData(AutoReplyReopenedMode.Custom, "Back")]
    public void The_reopened_mode_picks_the_text(string mode, string? expected)
    {
        Assert.Equal(expected, AutoReplyRules.Choose(Settings(mode: mode, reopened: "Back"), AutoReplyTrigger.Reopened, false).Text);
        // "Same message" uses the text even while the new-conversation reply itself is off.
        if (mode == AutoReplyReopenedMode.Same)
            Assert.Equal("Hi", AutoReplyRules.Choose(Settings(newOn: false, mode: mode), AutoReplyTrigger.Reopened, false).Text);
    }

    [Fact]
    public void While_the_ai_bot_answers_nothing_is_sent_unless_the_channel_asks_for_it()
    {
        Assert.Equal(AutoReplyChoice.Skip(AutoReplyRules.SkipAiAnswers), AutoReplyRules.Choose(Settings(), AutoReplyTrigger.NewConversation, true));
        Assert.Equal(AutoReplyChoice.Send("Hi"), AutoReplyRules.Choose(Settings(withAi: true), AutoReplyTrigger.NewConversation, true));
    }

    [Fact]
    public void No_settings_or_an_unknown_trigger()
    {
        Assert.Equal(AutoReplyChoice.Skip(AutoReplyRules.SkipOff), AutoReplyRules.Choose(null, AutoReplyTrigger.Reopened, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoReplyRules.Choose(Settings(), "later", false));
    }
}
