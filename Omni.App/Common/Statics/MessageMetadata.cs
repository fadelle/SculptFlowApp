namespace Omni.App.Common.Statics;

/// <summary>Fixed messages.metadata_json values the app writes itself.</summary>
public static class MessageMetadata
{
    /// <summary>An automatic message the contact received (opt-out confirmation, booking reminder): sender and origin
    /// "system" like the team's internal markers, but inbox.js shows it as a bubble.</summary>
    public const string Automatic = "{\"automatic\":true}";
}
