using System.Text.RegularExpressions;

namespace Omni.App.Tests.Guards;

/// <summary>
/// Dates and numbers a page shows follow the viewer's language (Common/Helpers/TextCulture: <c>value.Display("N0")</c>,
/// <c>TextCulture.Date(day, DateStyle.Date)</c>). CurrentCulture is always invariant, so a display pattern formatted
/// without a culture would show English month names and separators to everyone. This fails on such a pattern in
/// Pages (Razor and page models): month or weekday names (MMM, ddd), grouped numbers (#,), numeric formats with digits
/// (N0, C2, P1, 0.#). Machine formats (yyyy-MM-dd, HH:mm, O, a Guid's "N") are fine. A line that passes a culture, or
/// carries <c>format-ok: &lt;reason&gt;</c>, is skipped.
/// </summary>
public partial class DisplayFormatTests
{
    private static readonly string AppRoot = Path.Combine(LeftoverNameScanner.FindRepoRoot(), "Omni.App");

    [Fact]
    public void Pages_format_shown_dates_and_numbers_in_the_viewers_language()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "Pages"), "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cshtml") || f.EndsWith(".cshtml.cs")))
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.Contains("Culture", StringComparison.Ordinal) || line.Contains("format-ok:", StringComparison.Ordinal)) continue;
                foreach (System.Text.RegularExpressions.Match match in FormatCall().Matches(line).Concat(Interpolated().Matches(line)))
                {
                    if (IsDisplayPattern(match.Groups[1].Value))
                        hits.Add($"{Path.GetRelativePath(AppRoot, file).Replace('\\', '/')}:{number}: {line.Trim()}");
                }
            }
        }
        Assert.True(hits.Count == 0, "Shown dates/numbers formatted without the viewer's culture (use .Display(\"…\") or TextCulture.Date):\n  "
                                     + string.Join("\n  ", hits));
    }

    [Theory]
    [InlineData("MMM d, yyyy", true)]
    [InlineData("ddd", true)]
    [InlineData("#,0.00", true)]
    [InlineData("N0", true)]
    [InlineData("C2", true)]
    [InlineData("0.#", true)]
    [InlineData("0", true)]
    [InlineData("yyyy-MM-dd", false)]
    [InlineData("HH:mm", false)]
    [InlineData("O", false)]
    [InlineData("N", false)]
    public void The_guard_tells_display_patterns_from_machine_ones(string pattern, bool display)
    {
        Assert.Equal(display, IsDisplayPattern(pattern));
    }

    private static bool IsDisplayPattern(string pattern) =>
        pattern.Contains("MMM", StringComparison.Ordinal) || pattern.Contains("ddd", StringComparison.Ordinal)
        || pattern.Contains("#,", StringComparison.Ordinal) || NumericPattern().IsMatch(pattern);

    /// <summary>.ToString("…") with no second argument.</summary>
    [GeneratedRegex("""\.ToString\(\s*"([^"]+)"\s*\)""")]
    private static partial Regex FormatCall();

    /// <summary>{value:pattern} inside an interpolated string.</summary>
    [GeneratedRegex("""\{[A-Za-z_][\w.?]*(?:\([^)]*\))?:([^}]+)\}""")]
    private static partial Regex Interpolated();

    [GeneratedRegex(@"^(?:[NCPF][0-9]+|0(?:\.[0#]+)?)$")]
    private static partial Regex NumericPattern();
}
