namespace Omni.App.Common.Statics;

/// <summary>The fixed reply languages a bot editor offers (two-letter codes, labels "bots.languages.{code}"), with the English
/// name the bot's instructions use. Any other bot language is automatic: the contact's own.</summary>
public static class BotLanguages
{
    public static readonly IReadOnlyDictionary<string, string> EnglishNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "English", ["ar"] = "Arabic", ["fr"] = "French", ["es"] = "Spanish", ["de"] = "German", ["it"] = "Italian",
        ["pt"] = "Portuguese", ["tr"] = "Turkish", ["ru"] = "Russian", ["fa"] = "Persian", ["hy"] = "Armenian", ["zh"] = "Chinese"
    };

    /// <summary>In the order the editor lists them.</summary>
    public static IEnumerable<string> Codes => EnglishNames.Keys;
}
