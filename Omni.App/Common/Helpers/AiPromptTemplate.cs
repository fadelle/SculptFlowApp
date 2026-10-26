using System.Reflection;
using System.Text.RegularExpressions;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Reads an AI prompt file (AiPrompts, embedded from Resources/ai) and fills its {{name}} placeholders. The files are
/// read once (they are part of the build, like the UI strings). Line breaks are always "\n", as in the n8n workflow,
/// whatever the checkout did to the files.
/// </summary>
public static partial class AiPromptTemplate
{
    private const string Prefix = "ai/";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Files = new(LoadAll);

    /// <summary>The file's text (a template, a tool description or a parameters schema).</summary>
    public static string Load(string file) =>
        Files.Value.TryGetValue(file, out var text)
            ? text
            : throw new KeyNotFoundException($"AI prompt file '{file}' isn't embedded (Resources/ai)."); // text-ok: programming error, never reaches a reader

    /// <summary>The placeholder names a template uses, in order of first use.</summary>
    public static IReadOnlyList<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>The template with every {{name}} replaced by its value. A placeholder without a value is a programming
    /// error (the prompt would reach the model with a hole in it), so it throws.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? value
            : throw new InvalidOperationException($"No value for the prompt placeholder {{{{{m.Groups[1].Value}}}}}.")); // text-ok: programming error

    /// <summary>Load + Render.</summary>
    public static string RenderFile(string file, IReadOnlyDictionary<string, string> values) => Render(Load(file), values);

    private static IReadOnlyDictionary<string, string> LoadAll()
    {
        var assembly = typeof(AiPromptTemplate).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files[name[Prefix.Length..]] = reader.ReadToEnd().Replace("\r\n", "\n");
        }
        return files;
    }

    [GeneratedRegex(@"\{\{([A-Za-z][A-Za-z0-9]*)\}\}")]
    private static partial Regex Placeholder();
}
