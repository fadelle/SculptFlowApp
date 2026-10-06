using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>Thrown by SendAiReplyAsync when the conversation is no longer in AI mode at send time
/// (staff took over after the webhook that triggered this reply, but before the AI finished) —
/// callers map this to HTTP 409 conversation_in_human_mode rather than sending anyway.</summary>
public class ConversationNotInAiModeException : Exception
{
    public string CurrentMode { get; }
    public ConversationNotInAiModeException(string currentMode)
        : base($"Conversation is no longer in AI mode (current mode: '{currentMode}') — not sending.")
    {
        CurrentMode = currentMode;
    }
}
