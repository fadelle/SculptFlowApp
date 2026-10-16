using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Omni.App.Tests.Guards;

/// <summary>
/// Finds old product/domain words (clinic, lead, appointment, SculptFlow…) in the repo's tracked files, for
/// <see cref="LeftoverNamesTests"/>. Skips binary files, paths and literal tokens listed in
/// <c>Guards/leftover-names-allowlist.txt</c>, and any line carrying a <c>leftover-ok: &lt;reason&gt;</c> marker.
/// </summary>
public static class LeftoverNameScanner
{
    public const string LineMarker = "leftover-ok:";
    public const string AllowlistPath = "Omni.App.Tests/Guards/leftover-names-allowlist.txt";

    /// <summary>Every old word and how it's matched. "lead" only as a word or identifier part (Lead, lead_, leadId,
    /// GetLeadsAsync), never inside "leading", "leader" or "mislead".</summary>
    public static readonly IReadOnlyDictionary<string, Regex> Words = new Dictionary<string, Regex>
    {
        ["clinic"] = Word("clinic"),
        ["lead"] = new(@"(?:(?<![A-Za-z])[Ll]|(?<=[a-z0-9])L)eads?(?![a-z])", RegexOptions.Compiled),
        ["procedure"] = Word("procedure"),
        ["appointment"] = Word("appointment"),
        ["staff"] = Word("staff"),
        ["patient"] = Word("patient"),
        ["surgeon"] = Word("surgeon"),
        ["surgery"] = Word("surgery"),
        ["consultation"] = Word("consultation"),
        ["sculptflow"] = Word("sculpt_?flow"),
        ["plasticsurgery"] = Word("plastic_?surgery"),
        ["ai_agent"] = Word("ai_?agent"),
        ["max_agents"] = Word("max_?agents"),
    };

    private static Regex Word(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Hit(string Word, string Path, int Line, string Text);

    public sealed record Allowlist(IReadOnlyList<(Regex Path, string[]? Words)> Paths, IReadOnlyList<string> Tokens)
    {
        public bool AllowsPath(string path, string word) =>
            Paths.Any(p => p.Path.IsMatch(path) && (p.Words is null || p.Words.Contains(word)));
    }

    /// <summary>The repo root: the first folder above the test output that holds Omni.App.slnx.</summary>
    public static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Omni.App.slnx"))) return dir.FullName;
        throw new DirectoryNotFoundException("Omni.App.slnx not found above the test output folder.");
    }

    /// <summary>Tracked files (git ls-files), with '/' separators.</summary>
    public static IReadOnlyList<string> TrackedFiles(string root)
    {
        var info = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git ls-files failed: {error}");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Parses the allowlist. Lines: <c>path: &lt;glob&gt;</c> (optionally <c>| word,word</c> to allow only those words
    /// there), <c>token: &lt;literal&gt;</c> (text removed before matching, anywhere), <c># comment</c>. Every entry
    /// should say why in a comment above it.
    /// </summary>
    public static Allowlist ParseAllowlist(IEnumerable<string> lines)
    {
        var paths = new List<(Regex, string[]?)>();
        var tokens = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("path:", StringComparison.Ordinal))
            {
                var parts = line[5..].Split('|', 2);
                var words = parts.Length == 2
                    ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : null;
                foreach (var word in words ?? [])
                    if (!Words.ContainsKey(word)) throw new FormatException($"Unknown word '{word}' in allowlist line: {raw}");
                paths.Add((Glob(parts[0].Trim()), words));
            }
            else if (line.StartsWith("token:", StringComparison.Ordinal))
                tokens.Add(line[6..].Trim());
            else
                throw new FormatException($"Allowlist line must start with 'path:', 'token:' or '#': {raw}");
        }
        return new Allowlist(paths, tokens);
    }

    /// <summary>Glob over '/' paths: ** = any depth, * = within one folder, ? = one character.</summary>
    public static Regex Glob(string glob)
    {
        var pattern = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                pattern.Append(".*");
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/') i++;
            }
            else if (c == '*') pattern.Append("[^/]*");
            else if (c == '?') pattern.Append("[^/]");
            else pattern.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(pattern.Append('$').ToString(), RegexOptions.IgnoreCase);
    }

    /// <summary>Every old-word hit in one file's text that the allowlist and line markers don't excuse.</summary>
    public static IEnumerable<Hit> Scan(string path, string text, Allowlist allowlist)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains(LineMarker, StringComparison.Ordinal)) continue;
            foreach (var token in allowlist.Tokens) line = line.Replace(token, " ", StringComparison.OrdinalIgnoreCase);
            foreach (var (word, regex) in Words)
            {
                if (allowlist.AllowsPath(path, word) || !regex.IsMatch(line)) continue;
                var text1 = lines[i].Trim();
                yield return new Hit(word, path, i + 1, text1.Length > 160 ? text1[..160] + "…" : text1);
            }
        }
    }

    /// <summary>Scans every tracked text file under <paramref name="root"/>.</summary>
    public static List<Hit> ScanRepo(string root, Allowlist allowlist)
    {
        var hits = new List<Hit>();
        foreach (var path in TrackedFiles(root))
        {
            var full = Path.Combine(root, path);
            if (!File.Exists(full)) continue; // deleted in the working tree, not yet committed
            var bytes = File.ReadAllBytes(full);
            if (Array.IndexOf(bytes, (byte)0) >= 0) continue; // binary (fonts, images)
            hits.AddRange(Scan(path, Encoding.UTF8.GetString(bytes), allowlist));
        }
        return hits;
    }
}
