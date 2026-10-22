using System.Text.RegularExpressions;

namespace Omni.App.Tests.Guards;

/// <summary>
/// Messages a client or member can read come from the strings files: in the client-facing services, providers and
/// document engine, an exception is thrown with a key (<c>throw new ArgumentException("area.errors.name")</c>), never an
/// English sentence. A message only a machine or an operator reads (an n8n contract, a setup error, an "impossible"
/// state) is marked on its line with <c>text-ok: &lt;reason&gt;</c>. Platform-admin, billing-admin, cache, config and
/// benchmark services are left out: the admin portal and n8n read them, in English.
/// </summary>
public partial class ServerMessageKeysTests
{
    private static readonly string AppRoot = Path.Combine(LeftoverNameScanner.FindRepoRoot(), "Omni.App");

    private static readonly string[] ScannedFolders =
        ["Business/Services", "Business/Providers/Channels", "Business/Providers/Media", "Business/Engines/Knowledge"];

    private static readonly string[] SkippedPaths =
    [
        "Business/Services/PlatformAdmin/", "Business/Services/Caching/", "Business/Services/Configuration/",
        "Business/Services/Billing/BillingService.cs", "Business/Services/Billing/PlanService.cs",
        "Business/Services/Billing/RateCardService.cs", "Business/Services/Billing/ProviderBillingService.cs",
        "Business/Services/Billing/SubscriptionService.cs", "Business/Services/Billing/AddonService.cs",
        "Business/Services/Knowledge/KnowledgeBenchmarkService"
    ];

    [Fact]
    public void Client_facing_exceptions_are_thrown_with_strings_file_keys()
    {
        var offenders = new List<string>();
        foreach (var folder in ScannedFolders)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, folder), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(AppRoot, file).Replace('\\', '/');
                if (SkippedPaths.Any(relative.StartsWith)) continue;
                var text = File.ReadAllText(file);
                var lines = text.Split('\n');
                foreach (System.Text.RegularExpressions.Match match in LiteralThrow().Matches(text))
                {
                    var literal = match.Groups["literal"];
                    if (KeyShape().IsMatch(literal.Value)) continue;
                    var line = text[..literal.Index].Count(c => c == '\n');
                    if (lines[line].Contains("text-ok:", StringComparison.Ordinal)) continue;
                    offenders.Add($"  {relative}:{line + 1}: {literal.Value}");
                }
            }
        }
        Assert.True(offenders.Count == 0,
            "Throw these with an en.json key (or mark a machine-only message with 'text-ok: <reason>'):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void The_scan_sees_sentences_and_lets_keys_through()
    {
        Assert.Matches(LiteralThrow(), "throw new ArgumentException(\n    $\"Name {x} is wrong.\");");
        Assert.Matches(KeyShape(), "contacts.errors.notFound");
        Assert.DoesNotMatch(KeyShape(), "Contact not found.");
    }

    [GeneratedRegex("""throw\s+new\s+\w+Exception\(\s*(?:\w+\s*,\s*)?\$?"(?<literal>[^"]*)""")]
    private static partial Regex LiteralThrow();

    [GeneratedRegex(@"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)+$")]
    private static partial Regex KeyShape();
}
