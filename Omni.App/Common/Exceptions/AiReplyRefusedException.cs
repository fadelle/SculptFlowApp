namespace Omni.App.Common.Exceptions;

/// <summary>Thrown by SendAiReplyAsync when billing refused the reply's ai_reply usage (no rate, or the included credit and
/// wallet can't cover it). Nothing was sent and the conversation was already handed to the team; the AI endpoint answers
/// 409 ai_reply_refused (like conversation_in_human_mode) so n8n stops.</summary>
public class AiReplyRefusedException : InvalidOperationException
{
    /// <summary>One of <see cref="Omni.App.Common.Enums.UsageFailureReason"/>.</summary>
    public string Reason { get; }

    public AiReplyRefusedException(string reason)
        : base($"The AI reply wasn't sent: billing refused it ({reason}); the conversation was handed to the team.") // text-ok: read by n8n
    {
        Reason = reason;
    }
}
