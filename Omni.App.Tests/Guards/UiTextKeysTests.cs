using System.Text.Json;
using System.Text.RegularExpressions;

namespace Omni.App.Tests.Guards;

/// <summary>
/// The UI strings files (Omni.App/Resources/i18n/*.json) and the code agree: every key the code uses exists in en.json,
/// every en.json key is used, and every placeholder is an argument ({0}) or a business-type term ({contact}…).
/// Keys are found where the code names them: <c>T["key"]</c> / <c>_text["key"]</c> / <c>T.Html("key")</c> (Razor and C#), an exception thrown
/// with a key as its message, a <c>…Key = "key"</c> constant, and <c>omniText.t('key')</c> in JS.
/// </summary>
public partial class UiTextKeysTests
{
    private static readonly string Root = LeftoverNameScanner.FindRepoRoot();
    private static readonly string AppRoot = Path.Combine(Root, "Omni.App");

    private static Dictionary<string, string> Strings(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppRoot, "Resources", "i18n", language + ".json")))!;

    /// <summary>Keys read by building their name in code (Terminology reads terms.{term}.one/other).</summary>
    private static bool ReadDynamically(string key) => key.StartsWith("terms.", StringComparison.Ordinal);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(AppRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".cshtml") || f.EndsWith(".js"))
            .Where(f => !Regex.IsMatch(f.Replace('\\', '/'), "/(bin|obj|wwwroot/lib)/"));

    private static readonly Lazy<Dictionary<string, List<string>>> UsedKeys = new(() =>
    {
        var used = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in SourceFiles())
        {
            // Comment lines may show the syntax with sample keys; only code counts.
            var text = string.Join('\n', File.ReadLines(file).Where(l => !IsComment(l.TrimStart())));
            var patterns = file.EndsWith(".js") ? [JsKey()] : new[] { IndexerKey(), HtmlKey(), ExceptionKey(), ConstantKey() };
            foreach (var match in patterns.SelectMany(p => p.Matches(text)))
            {
                var key = match.Groups[1].Value;
                if (!used.TryGetValue(key, out var files)) used[key] = files = [];
                files.Add(Path.GetRelativePath(Root, file).Replace('\\', '/'));
            }
        }
        return used;
    });

    private static bool IsComment(string line) =>
        line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("@*", StringComparison.Ordinal) || line.StartsWith('*');

    [Fact]
    public void The_scan_finds_the_keys_the_code_uses()
    {
        Assert.Contains("signup.businessType.label", UsedKeys.Value.Keys);          // Razor @T["…"]
        Assert.Contains("businessTypes.errors.notFound", UsedKeys.Value.Keys);      // thrown by a service
        Assert.Contains("signup.errors.businessTypeRequired", UsedKeys.Value.Keys); // a …Key constant
    }

    [Fact]
    public void Every_key_used_in_code_exists_in_english()
    {
        var english = Strings("en");
        var missing = UsedKeys.Value.Where(u => !english.ContainsKey(u.Key)).Select(u => $"  {u.Key} ({string.Join(", ", u.Value.Distinct())})").ToList();
        Assert.True(missing.Count == 0, "Keys used in code but missing from en.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Every_english_key_is_used()
    {
        var unused = Strings("en").Keys.Where(k => !ReadDynamically(k) && !UsedKeys.Value.ContainsKey(k)).ToList();
        Assert.True(unused.Count == 0, "Keys in en.json no code uses (remove them):\n  " + string.Join("\n  ", unused));
    }

    [Fact]
    public void Every_term_has_a_default_word()
    {
        var english = Strings("en");
        Assert.All(TermName.All, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(english.GetValueOrDefault($"terms.{t}.one")), $"terms.{t}.one");
            Assert.False(string.IsNullOrWhiteSpace(english.GetValueOrDefault($"terms.{t}.other")), $"terms.{t}.other");
        });
    }

    [Fact]
    public void Keys_are_grouped_and_placeholders_are_known_in_every_language()
    {
        var english = Strings("en");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "Resources", "i18n"), "*.json"))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            Assert.Contains(language, AppLanguage.All);
            foreach (var (key, text) in Strings(language))
            {
                Assert.Matches(KeyShape(), key);
                Assert.True(english.ContainsKey(key), $"{language}.json has '{key}', which en.json doesn't.");
                var unknown = TextFormat.PlaceholdersIn(text).Where(p => !p.All(char.IsAsciiDigit) && !TermPlaceholders.Names.Contains(p)).ToList();
                Assert.True(unknown.Count == 0, $"{language}.json '{key}' uses unknown placeholder(s): {string.Join(", ", unknown)}");
            }
        }
    }

    [GeneratedRegex("""\b(?:T|_text|Text|_localizer|Localizer)\[\s*"([^"]+)"\s*[,\]]""")]
    private static partial Regex IndexerKey();

    [GeneratedRegex("""\b(?:T|_text|Text)\.Html\(\s*"([^"]+)"\s*[,)]""")]
    private static partial Regex HtmlKey();

    [GeneratedRegex("""Exception\(\s*"([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"\s*\)""")]
    private static partial Regex ExceptionKey();

    [GeneratedRegex("""\w*Key\s*=\s*"([a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+)"\s*;""")]
    private static partial Regex ConstantKey();

    [GeneratedRegex("""omniText\.t\(\s*['"]([^'"]+)['"]""")]
    private static partial Regex JsKey();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)+$")]
    private static partial Regex KeyShape();
}
