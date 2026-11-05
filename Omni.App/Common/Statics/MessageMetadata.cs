namespace Omni.App.Common.Statics;

/// <summary>Fixed messages.metadata_json values the app writes itself.</summary>
public static class MessageMetadata
{
    /// <summary>An automatic message the contact received (opt-out confirmation, booking reminder): sender and origin
    /// "system" like the team's internal markers, but inbox.js shows it as a bubble.</summary>
    public const string Automatic = "{\"automatic\":true}";

    /// <summary>The Inbox marker "handed from AI to your team" (QA t7-07: inbox.js words it in the reader's language from the key;
    /// the English content is only for readers without the key, e.g. an export).</summary>
    public const string HandedToTeamMarker = "{\"marker\":\"handed_to_team\"}";

    /// <summary>The Inbox marker "returned to AI".</summary>
    public const string ReturnedToAiMarker = "{\"marker\":\"returned_to_ai\"}";
}
