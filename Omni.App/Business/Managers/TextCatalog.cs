using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Enums;

namespace Omni.App.Business.Managers;

public class TextCatalog : ITextCatalog
{
    /// <summary>Embedded resource names are "i18n/{language}.json" (see Omni.App.csproj).</summary>
    private const string ResourcePrefix = "i18n/";

    private static readonly JsonSerializerOptions ScriptJson = new() { Encoder = JavaScriptEncoder.Default };

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _versions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _scripts = new(StringComparer.Ordinal);

    /// <summary>Loads the strings files embedded in the app.</summary>
    public TextCatalog() : this(LoadEmbedded())
    {
    }

    /// <summary>From raw JSON per language (tests). Throws when a file isn't a flat object of strings, or English is missing.</summary>
    public TextCatalog(IReadOnlyDictionary<string, string> jsonByLanguage)
    {
        foreach (var (language, json) in jsonByLanguage) _strings[language] = Parse(language, json);
        if (!_strings.TryGetValue(AppLanguage.English, out var english)) throw new InvalidOperationException("The English strings file (en.json) is missing.");
        foreach (var (language, strings) in _strings)
        {
            // The browser copy of a translation carries English for the keys it doesn't have yet (the server falls back
            // per key the same way), and its version covers both files.
            var browser = language == AppLanguage.English
                ? strings
                : english.Concat(strings).GroupBy(p => p.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
            var versionSource = language == AppLanguage.English ? jsonByLanguage[language] : jsonByLanguage[AppLanguage.English] + "\n" + jsonByLanguage[language];
            _versions[language] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(versionSource)))[..12];
            _scripts[language] = "window.omniStrings = " + JsonSerializer.Serialize(browser, ScriptJson) + ";\n";
        }
        Languages = _strings.Keys.OrderBy(l => l == AppLanguage.English ? 0 : 1).ThenBy(l => l, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> Languages { get; }

    public bool Supports(string language) => _strings.ContainsKey(language);

    public string? Find(string language, string key) =>
        _strings.TryGetValue(language, out var strings) && strings.TryGetValue(key, out var text) ? text : null;

    public IReadOnlyDictionary<string, string> All(string language) =>
        _strings.TryGetValue(language, out var strings) ? strings : new Dictionary<string, string>();

    public string Version(string language) => _versions.GetValueOrDefault(language, string.Empty);

    public string Script(string language) => _scripts.GetValueOrDefault(language, "window.omniStrings = {};\n");

    private static Dictionary<string, string> LoadEmbedded()
    {
        var assembly = typeof(TextCatalog).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            files[Path.GetFileNameWithoutExtension(name[ResourcePrefix.Length..])] = reader.ReadToEnd();
        }
        return files;
    }

    private static Dictionary<string, string> Parse(string language, string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"{language}.json must be one JSON object of key → text.");
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException($"{language}.json: \"{property.Name}\" must be a string.");
            if (!strings.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidOperationException($"{language}.json: \"{property.Name}\" appears twice.");
        }
        return strings;
    }
}
