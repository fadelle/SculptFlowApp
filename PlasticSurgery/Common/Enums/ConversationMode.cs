namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for Conversation.Mode — must match the CHECK constraint in Database/schema.sql.</summary>
public static class ConversationMode
{
    /// <summary>AI/n8n is allowed to automatically reply.</summary>
    public const string Ai = "ai";
    /// <summary>Messages are saved and shown, but AI must not automatically reply — staff is handling it.</summary>
    public const string Human = "human";
    /// <summary>AI may draft a suggested reply, but staff approval is required before it's sent.</summary>
    public const string Approval = "approval";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Ai, Human, Approval };
}
