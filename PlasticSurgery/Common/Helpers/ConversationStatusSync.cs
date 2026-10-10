using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Common.Helpers;

/// <summary>Single place that brings a closed conversation back to active. A conversation is one per lead per
/// channel and is never recreated, so without this a patient writing again into a conversation staff had closed
/// would stay filed under "Closed" (and drop out of the dashboard's needs-attention count). Used by
/// ConversationService (staff Reopen, and the older generic add-message path) and MessageService (a patient
/// message arriving from any channel).</summary>
public static class ConversationStatusSync
{
    /// <summary>Sets the conversation active if it isn't already. Returns true only when something changed, so
    /// callers log/broadcast a reopen exactly once and treat an already-active conversation as a no-op.</summary>
    public static bool ReopenIfNotActive(Conversation conversation, DateTimeOffset at)
    {
        if (conversation.Status == ConversationStatus.Active) return false;

        conversation.Status = ConversationStatus.Active;
        conversation.UpdatedAt = at;
        return true;
    }
}
