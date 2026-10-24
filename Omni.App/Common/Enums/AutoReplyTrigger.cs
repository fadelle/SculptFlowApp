namespace Omni.App.Common.Enums;

/// <summary>Why an automatic reply was considered: the contact's first message in a conversation, or a message that reopened a
/// closed one. Stored in the auto_reply_sent event's metadata ({"trigger": ...}).</summary>
public static class AutoReplyTrigger
{
    public const string NewConversation = "new";
    public const string Reopened = "reopened";
}
