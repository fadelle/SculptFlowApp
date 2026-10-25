namespace Omni.App.Common.Enums;

/// <summary>crm.bot_test_messages.role: who wrote a line of a bot test conversation.</summary>
public static class BotTestRole
{
    /// <summary>The person testing, playing the contact.</summary>
    public const string Contact = "contact";

    /// <summary>The bot's reply.</summary>
    public const string Bot = "bot";

    /// <summary>A message Omni itself would send the contact (the handoff message).</summary>
    public const string Automatic = "automatic";

    /// <summary>What happened behind the scenes: a simulated booking, a contact update, a handoff. Never sent to anyone.</summary>
    public const string Note = "note";
}
