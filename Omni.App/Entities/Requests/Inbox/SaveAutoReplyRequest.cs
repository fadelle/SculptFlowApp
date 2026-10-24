namespace Omni.App.Entities.Requests.Inbox;

/// <summary>A client's automatic reply on one channel. ReopenedMode is an AutoReplyReopenedMode value; a message is required
/// for each reply that is on.</summary>
public record SaveAutoReplyRequest(string Channel, bool NewConversationEnabled, string? NewConversationMessage, string ReopenedMode,
    string? ReopenedMessage, bool SendWhenAiAnswers);
