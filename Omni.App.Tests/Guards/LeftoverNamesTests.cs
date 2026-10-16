namespace Omni.App.Tests.Guards;

/// <summary>
/// Milestone A guard: no old product/domain word (clinic, lead, appointment, SculptFlow, PlasticSurgery…) is left in a
/// tracked file outside the allowlist (<c>leftover-names-allowlist.txt</c>), a <c>leftover-ok: &lt;reason&gt;</c> line
/// marker, or a pending scope below. Each rename session shrinks <see cref="PendingScopes"/>; a pending scope with no
/// hits left fails too, so the list can't go stale. At the end of Milestone A the list is empty.
/// </summary>
public class LeftoverNamesTests
{
    /// <summary>Word × path glob still waiting for its rename session (see milestone-a-plan.md, section 5).</summary>
    public static readonly (string Word, string Glob, string ClosedBy)[] PendingScopes =
    [
        ("procedure", "**", "S4"),
        ("appointment", "**", "S5"),
        ("consultation", "**", "S5"),
        ("lead", "**", "S6"),
        ("patient", "**", "S6"),
        ("surgeon", "**", "S6"),
        ("surgery", "**", "S6"),
        ("staff", "**", "S7"),
        ("ai_agent", "**", "S7"),
        ("max_agents", "**", "S7"),
    ];

    private static readonly Lazy<(List<LeftoverNameScanner.Hit> Hits, LeftoverNameScanner.Allowlist Allowlist)> Scan = new(() =>
    {
        var root = LeftoverNameScanner.FindRepoRoot();
        var allowlist = LeftoverNameScanner.ParseAllowlist(File.ReadAllLines(Path.Combine(root, LeftoverNameScanner.AllowlistPath)));
        return (LeftoverNameScanner.ScanRepo(root, allowlist), allowlist);
    });

    private static bool InPendingScope(LeftoverNameScanner.Hit hit) =>
        PendingScopes.Any(s => s.Word == hit.Word && LeftoverNameScanner.Glob(s.Glob).IsMatch(hit.Path));

    [Fact]
    public void No_old_name_is_left_outside_the_allowlist_and_pending_scopes()
    {
        var leftovers = Scan.Value.Hits.Where(h => !InPendingScope(h)).ToList();
        Assert.True(leftovers.Count == 0,
            $"{leftovers.Count} old name(s) left. Rename them, or (rarely) add 'leftover-ok: <reason>' to the line:\n" +
            string.Join("\n", leftovers.Take(60).Select(h => $"  {h.Path}:{h.Line} [{h.Word}] {h.Text}")));
    }

    [Fact]
    public void Every_pending_scope_still_has_hits()
    {
        var stale = PendingScopes
            .Where(s => !Scan.Value.Hits.Any(h => h.Word == s.Word && LeftoverNameScanner.Glob(s.Glob).IsMatch(h.Path)))
            .Select(s => $"  ({s.Word}, {s.Glob})")
            .ToList();
        Assert.True(stale.Count == 0, "These pending scopes have no hits left; remove them from PendingScopes:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Pending_scopes_name_known_words()
    {
        Assert.All(PendingScopes, s => Assert.True(LeftoverNameScanner.Words.ContainsKey(s.Word), $"Unknown word '{s.Word}'."));
    }

    [Theory]
    [InlineData("var lead = 1;", true)]
    [InlineData("crm.leads", true)]
    [InlineData("lead_id", true)]
    [InlineData("leadId", true)]
    [InlineData("GetLeadsAsync", true)]
    [InlineData("LeadDetail", true)]
    [InlineData("the leading edge", false)]
    [InlineData("team leader", false)]
    [InlineData("do not mislead", false)]
    [InlineData("pleaded", false)]
    public void Lead_matches_words_and_identifier_parts_only(string text, bool expected)
    {
        Assert.Equal(expected, LeftoverNameScanner.Words["lead"].IsMatch(text));
    }

    [Theory]
    [InlineData("SculptFlow", "sculptflow")]
    [InlineData("SCULPTFLOW_TEST_DB", "sculptflow")]
    [InlineData("sculpt_flow", "sculptflow")]
    [InlineData("namespace PlasticSurgery.Business;", "plasticsurgery")]
    [InlineData("ClinicId", "clinic")]
    [InlineData("AiAgent", "ai_agent")]
    [InlineData("max_agents", "max_agents")]
    public void Old_words_match_in_any_case(string text, string word)
    {
        Assert.Matches(LeftoverNameScanner.Words[word], text);
    }

    [Fact]
    public void Line_marker_tokens_and_paths_excuse_hits()
    {
        var allowlist = LeftoverNameScanner.ParseAllowlist(
        [
            "# comment",
            "path: docs/history/**",
            "path: Database/seed-demo.sql | clinic",
            "token: example.sculptflow.test",
        ]);
        const string text = "clinic one\nsee example.sculptflow.test\nstored procedure -- leftover-ok: SQL term\nSculptFlow";

        Assert.Empty(LeftoverNameScanner.Scan("docs/history/a/b.md", text, allowlist));
        Assert.Equal(["sculptflow"], LeftoverNameScanner.Scan("Database/seed-demo.sql", text, allowlist).Select(h => h.Word));
        var hits = LeftoverNameScanner.Scan("Omni.App/x.cs", text, allowlist).ToList();
        Assert.Equal([("clinic", 1), ("sculptflow", 4)], hits.Select(h => (h.Word, h.Line)));
    }

    [Fact]
    public void Allowlist_rejects_unknown_lines_and_words()
    {
        Assert.Throws<FormatException>(() => LeftoverNameScanner.ParseAllowlist(["Database/**"]));
        Assert.Throws<FormatException>(() => LeftoverNameScanner.ParseAllowlist(["path: a.sql | nope"]));
    }

    [Theory]
    [InlineData("Database/migrations/**", "Database/migrations/2026-10/copy.sql", true)]
    [InlineData("Database/migrations/**", "Database/schema.sql", false)]
    [InlineData("Omni.App/Pages/*.cshtml", "Omni.App/Pages/Index.cshtml", true)]
    [InlineData("Omni.App/Pages/*.cshtml", "Omni.App/Pages/Shared/_Layout.cshtml", false)]
    [InlineData("**", "README.md", true)]
    [InlineData("**/*.js", "Omni.App/wwwroot/js/inbox.js", true)]
    public void Globs_match_tracked_paths(string glob, string path, bool expected)
    {
        Assert.Equal(expected, LeftoverNameScanner.Glob(glob).IsMatch(path));
    }
}
