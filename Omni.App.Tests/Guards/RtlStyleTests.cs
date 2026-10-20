using System.Text.RegularExpressions;
using Omni.App.Common.Helpers;

namespace Omni.App.Tests.Guards;

/// <summary>
/// Arabic pages render right to left (<c>&lt;html dir="rtl"&gt;</c>), so the styles never name a physical side
/// (docs/UI_GUIDE.md §0.4): <c>margin-inline-start</c>, not <c>margin-left</c>; <c>inset-inline-end</c>, not <c>right</c>;
/// <c>text-align: start</c>; logical corner radii; no 4-value margin/padding/inset whose left and right differ. A line that
/// must stay physical (a <c>[dir='rtl']</c> override, <c>background-position</c>) carries <c>rtl-ok: &lt;reason&gt;</c>.
/// The same holds for <c>style="…"</c> in Razor and styles set from scripts, and directional icons carry
/// <c>au-icon--dir</c> so they mirror.
/// </summary>
public partial class RtlStyleTests
{
    private static readonly string AppRoot = Path.Combine(LeftoverNameScanner.FindRepoRoot(), "Omni.App");

    private static readonly HashSet<string> FourValueShorthands = new(StringComparer.Ordinal)
    {
        "margin", "padding", "inset", "border-width", "border-color", "border-style", "scroll-margin", "scroll-padding",
    };

    [Fact]
    public void Stylesheets_use_logical_sides()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "wwwroot", "css"), "*.css", SearchOption.AllDirectories))
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.Contains("rtl-ok:", StringComparison.Ordinal)) continue;
                foreach (var problem in Problems(Comment().Replace(line, "")))
                    hits.Add($"{Path.GetRelativePath(AppRoot, file).Replace('\\', '/')}:{number}: {problem}: {line.Trim()}");
            }
        }
        Assert.True(hits.Count == 0, "Physical left/right in CSS (use logical properties, or mark the line rtl-ok: <reason>):\n  "
                                     + string.Join("\n  ", hits));
    }

    [Fact]
    public void Razor_inline_styles_and_scripts_use_logical_sides()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "Pages"), "*.cshtml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(AppRoot, "wwwroot", "js"), "*.js", SearchOption.AllDirectories)))
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.Contains("rtl-ok:", StringComparison.Ordinal)) continue;
                var problems = StyleAttribute().Matches(line)
                    .SelectMany(m => Problems(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)).ToList();
                if (ScriptSide().IsMatch(line)) problems.Add("physical side set from a script");
                foreach (var problem in problems)
                    hits.Add($"{Path.GetRelativePath(AppRoot, file).Replace('\\', '/')}:{number}: {problem}: {line.Trim()}");
            }
        }
        Assert.True(hits.Count == 0, "Physical left/right in markup or scripts (use logical properties, or mark the line rtl-ok: <reason>):\n  "
                                     + string.Join("\n  ", hits));
    }

    [Fact]
    public void Directional_icons_mirror_in_right_to_left_pages()
    {
        var glyphs = string.Join("|", IconDirection.Glyphs.Select(Regex.Escape));
        var icon = new Regex("<span class=\"([^\"]*\\bau-icon\\b[^\"]*)\"[^>]*>(" + glyphs + ")</span>");
        var scripted = new Regex("icon\\(\\s*'(" + glyphs + ")'\\s*(?:,\\s*'([^']*)')?\\)");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "Pages"), "*.cshtml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(Path.Combine(AppRoot, "wwwroot", "js"), "*.js", SearchOption.AllDirectories)))
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                var missing = icon.Matches(line).Any(m => !m.Groups[1].Value.Contains("au-icon--dir", StringComparison.Ordinal))
                              || scripted.Matches(line).Any(m => !m.Groups[2].Value.Contains("au-icon--dir", StringComparison.Ordinal));
                if (missing) hits.Add($"{Path.GetRelativePath(AppRoot, file).Replace('\\', '/')}:{number}: {line.Trim()}");
            }
        }
        Assert.True(hits.Count == 0, "Directional icon without au-icon--dir (or IconDirection.ClassFor for a computed glyph):\n  "
                                     + string.Join("\n  ", hits));
    }

    [Theory]
    [InlineData("margin-left: 4px;", true)]
    [InlineData("  padding-right: 36px;", true)]
    [InlineData(".x { position: absolute; right: 0; }", true)]
    [InlineData("left: 50%;", true)]
    [InlineData("border-left: 1px solid red;", true)]
    [InlineData("border-right-color: transparent;", true)]
    [InlineData("border-top-left-radius: 4px;", true)]
    [InlineData("text-align: right;", true)]
    [InlineData("float: left;", true)]
    [InlineData("background-position: right 8px center;", true)]
    [InlineData("padding: 6px 8px 6px 16px;", true)]
    [InlineData("inset: 0 0 0 auto;", true)]
    [InlineData("border-radius: 0 3px 3px 0;", true)]
    [InlineData("border-radius: 4px 0;", true)]
    [InlineData("border-radius: 4px 4px 0;", true)]
    [InlineData("margin-inline-start: 4px;", false)]
    [InlineData("inset-inline-end: 0;", false)]
    [InlineData("text-align: start;", false)]
    [InlineData("padding: 6px 8px;", false)]
    [InlineData("padding: 8px 10px 4px;", false)]
    [InlineData("margin: 0 auto 0 auto;", false)]
    [InlineData("border-radius: 0 0 8px 8px;", false)]
    [InlineData("border-start-end-radius: 3px;", false)]
    [InlineData("transition: border-color 0.2s, left 0.2s;", false)]
    [InlineData(".hint-line.right { color: red; }", false)]
    [InlineData("background-position: center;", false)]
    public void The_guard_tells_physical_sides_from_logical_ones(string css, bool physical)
    {
        Assert.Equal(physical, Problems(css).Any());
    }

    [Theory]
    [InlineData("arrow_back", "au-icon au-icon--dir")]
    [InlineData("send", "au-icon au-icon--dir")]
    [InlineData("play_arrow", "au-icon")]
    [InlineData(null, "au-icon")]
    public void IconDirection_marks_only_directional_glyphs(string? glyph, string classes)
    {
        Assert.Equal(classes, IconDirection.ClassFor(glyph));
    }

    private static IEnumerable<string> Problems(string css)
    {
        foreach (System.Text.RegularExpressions.Match d in Declaration().Matches(css))
        {
            var property = d.Groups[1].Value;
            var value = d.Groups[2].Value.Replace("!important", "", StringComparison.Ordinal).Trim();
            if (PhysicalProperty().IsMatch(property))
                yield return "physical property";
            else if (property is "text-align" or "float" or "clear" or "background-position" && SideWord().IsMatch(value))
                yield return "physical value";
            else if (FourValueShorthands.Contains(property) && Values(value) is [_, var right, _, var left] && right != left)
                yield return "left and right differ";
            else if (property == "border-radius" && Values(value.Split('/')[0]) is var r
                     && ((r.Length == 2 && r[0] != r[1]) || (r.Length == 3 && (r[0] != r[1] || r[2] != r[1]))
                         || (r.Length == 4 && (r[0] != r[1] || r[2] != r[3]))))
                yield return "corners differ by side";
        }
    }

    /// <summary>Space-separated values, keeping functions such as calc(a + b) whole.</summary>
    private static string[] Values(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = "";
        foreach (var ch in value.Trim())
        {
            if (ch == '(') depth++;
            if (ch == ')') depth--;
            if (ch == ' ' && depth == 0)
            {
                if (current.Length > 0) parts.Add(current);
                current = "";
            }
            else current += ch;
        }
        if (current.Length > 0) parts.Add(current);
        return [.. parts];
    }

    /// <summary>A declaration: property at a line start or after { ; or whitespace, then its value up to ; or }.</summary>
    [GeneratedRegex(@"(?:^|[;{\s])([a-z-]+)\s*:\s*([^;{}]+?)\s*(?=;|}|$)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^(?:(?:margin|padding|scroll-margin|scroll-padding)-(?:left|right)|border-(?:left|right)(?:-(?:width|color|style))?|border-(?:top|bottom)-(?:left|right)-radius|left|right)$")]
    private static partial Regex PhysicalProperty();

    [GeneratedRegex(@"\b(?:left|right)\b")]
    private static partial Regex SideWord();

    [GeneratedRegex(@"/\*.*?\*/")]
    private static partial Regex Comment();

    [GeneratedRegex("""\bstyle="([^"]*)"|\bstyle='([^']*)'""")]
    private static partial Regex StyleAttribute();

    /// <summary>element.style.left = …, style.marginRight, style.setProperty('padding-left', …).</summary>
    [GeneratedRegex(@"\.style\.(?:left|right|(?:margin|padding|border)(?:Left|Right)\w*)\b|setProperty\(\s*['""](?:left|right|(?:margin|padding|border)-(?:left|right)[\w-]*)['""]")]
    private static partial Regex ScriptSide();
}
