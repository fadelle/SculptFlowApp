namespace Omni.App.Entities.Models;

/// <summary>A client's automatic reply on one channel (crm.auto_replies; no row = off). Read and changed through
/// Business/Services/Inbox/AutoReplyService; sent by MessageService.IngestAsync.</summary>
public class AutoReply
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>ConversationChannel value (whatsapp, telegram).</summary>
    public string Channel { get; set; } = "";

    /// <summary>Reply to the contact's first message in a conversation.</summary>
    public bool NewConversationEnabled { get; set; }
    public string? NewConversationMessage { get; set; }

    /// <summary>AutoReplyReopenedMode: off, same (the new-conversation message) or custom (ReopenedMessage).</summary>
    public string ReopenedMode { get; set; } = Common.Enums.AutoReplyReopenedMode.Off;
    public string? ReopenedMessage { get; set; }

    /// <summary>Also send it while the AI bot answers the conversation (off: only when the team answers it).</summary>
    public bool SendWhenAiAnswers { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
