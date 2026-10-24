namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>A client's automatic reply on one channel (the defaults when it has no row: everything off).</summary>
public record AutoReplyDto(string Channel, bool NewConversationEnabled, string? NewConversationMessage, string ReopenedMode,
    string? ReopenedMessage, bool SendWhenAiAnswers);
