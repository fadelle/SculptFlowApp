namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// The UI strings files (Resources/i18n/{language}.json, embedded in the app): one flat JSON object per language,
/// key → text, keys grouped "area.element.state" ("signup.businessType.label"). Texts may hold {0}-style arguments and
/// business-type terms ({contact}, {Contacts}…). Loaded once at start-up; English is the fallback for a missing key.
/// </summary>
public interface ITextCatalog
{
    /// <summary>Languages that have a strings file, English first.</summary>
    IReadOnlyList<string> Languages { get; }

    bool Supports(string language);

    /// <summary>The raw text of a key in exactly this language, or null.</summary>
    string? Find(string language, string key);

    /// <summary>Every key of one language (empty for an unknown language).</summary>
    IReadOnlyDictionary<string, string> All(string language);

    /// <summary>A short hash of the language's file, for cache-busting the browser copy.</summary>
    string Version(string language);

    /// <summary>The browser copy: a script that sets window.omniStrings to the language's raw texts, English filling the keys
    /// a translation doesn't have yet (served at
    /// /i18n/{language}.js; wwwroot/js/omni-text.js fills the placeholders).</summary>
    string Script(string language);
}
