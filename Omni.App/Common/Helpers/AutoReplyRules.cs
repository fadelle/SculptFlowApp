using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Inbox;

namespace Omni.App.Common.Helpers;

/// <summary>Which automatic reply (if any) a contact's message gets, from the channel's settings. Pure, so the Inbox and the
/// tests share one rule.</summary>
public static class AutoReplyRules
{
    public const string SkipOff = "off";
    public const string SkipAiAnswers = "ai_answers";
    public const string SkipDisabledByAdmin = "disabled_by_admin";
    /// <summary>The client is deactivated: its incoming messages are still saved, but nothing automatic goes out (QA t12-fix-02).</summary>
    public const string SkipClientInactive = "client_inactive";

    /// <param name="settings">The channel's settings; null = the client has none (off).</param>
    /// <param name="trigger">AutoReplyTrigger.NewConversation or AutoReplyTrigger.Reopened.</param>
    /// <param name="aiAnswers">The AI bot is asked to answer this message.</param>
    public static AutoReplyChoice Choose(AutoReplyDto? settings, string trigger, bool aiAnswers)
    {
        if (settings is null) return AutoReplyChoice.Skip(SkipOff);
        var text = trigger switch
        {
            AutoReplyTrigger.NewConversation => settings.NewConversationEnabled ? settings.NewConversationMessage : null,
            AutoReplyTrigger.Reopened => settings.ReopenedMode switch
            {
                // "Same message" is the new-conversation reply: off when that is off (QA t7-09).
                AutoReplyReopenedMode.Same => settings.NewConversationEnabled ? settings.NewConversationMessage : null,
                AutoReplyReopenedMode.Custom => settings.ReopenedMessage,
                _ => null
            },
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null)
        };
        if (string.IsNullOrWhiteSpace(text)) return AutoReplyChoice.Skip(SkipOff);
        // The bot greets the contact itself, so by default the automatic reply is only for conversations the team answers.
        if (aiAnswers && !settings.SendWhenAiAnswers) return AutoReplyChoice.Skip(SkipAiAnswers);
        return AutoReplyChoice.Send(text.Trim());
    }
}
