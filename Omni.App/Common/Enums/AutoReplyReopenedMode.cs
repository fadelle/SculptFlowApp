namespace Omni.App.Common.Enums;

/// <summary>crm.auto_replies.reopened_mode: what a contact gets when they write to a conversation that was closed.</summary>
public static class AutoReplyReopenedMode
{
    /// <summary>No automatic reply.</summary>
    public const string Off = "off";

    /// <summary>The new-conversation message text (also while the new-conversation reply itself is off).</summary>
    public const string Same = "same";

    /// <summary>Its own message (reopened_message).</summary>
    public const string Custom = "custom";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Off, Same, Custom };
}
