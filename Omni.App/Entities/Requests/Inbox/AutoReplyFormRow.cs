namespace Omni.App.Entities.Requests.Inbox;

/// <summary>One channel's fields on the Auto-respond page's single form (posted as Replies[i].*); unchecked switches post
/// nothing, so they default to false.</summary>
public class AutoReplyFormRow
{
    public string Channel { get; set; } = "";
    public bool NewConversationEnabled { get; set; }
    public string? NewConversationMessage { get; set; }
    public string ReopenedMode { get; set; } = "off";
    public string? ReopenedMessage { get; set; }
    public bool SendWhenAiAnswers { get; set; }

    public SaveAutoReplyRequest ToRequest() =>
        new(Channel, NewConversationEnabled, NewConversationMessage, ReopenedMode, ReopenedMessage, SendWhenAiAnswers);
}
