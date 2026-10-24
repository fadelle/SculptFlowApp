namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>Whether a contact's message gets an automatic reply: Text to send, or null with SkipReason (for the message
/// trace: AutoReplyRules.Skip*).</summary>
public record AutoReplyChoice(string? Text, string? SkipReason)
{
    public static AutoReplyChoice Send(string text) => new(text, null);
    public static AutoReplyChoice Skip(string reason) => new(null, reason);
}
