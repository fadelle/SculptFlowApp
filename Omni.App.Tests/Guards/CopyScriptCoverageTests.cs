using System.Text.RegularExpressions;

namespace Omni.App.Tests.Guards;

/// <summary>
/// Database/migrations/2026-10-generic/copy-from-old.sql moves the old project's data into a new one, table by table. When
/// schema.sql gains or loses a table this fails until the script (and its test, test-copy.ps1) follow; the script itself is
/// run end to end by test-copy.ps1, which needs a throwaway PostgreSQL and is not part of dotnet test.
/// </summary>
public class CopyScriptCoverageTests
{
    // Seeded by schema.sql in the new project, so the script never copies them.
    private static readonly string[] SeededBySchema = ["core.business_types", "crm.pipeline_stages"];
    // The script creates this one itself (it belongs to the admin portal's schema file, not schema.sql).
    private static readonly string[] CreatedByScript = ["admin.admin_audit_log"];
    // New in Milestone C: the old project has nothing to copy into them.
    private static readonly string[] NotInOldProject =
        ["identity.account_tokens", "scheduling.reminder_settings", "scheduling.booking_reminders", "billing.addons", "billing.client_addons",
         "crm.auto_replies"];

    private static string Root => LeftoverNameScanner.FindRepoRoot();

    private static string ReadScript() =>
        File.ReadAllText(Path.Combine(Root, "Database", "migrations", "2026-10-generic", "copy-from-old.sql"));

    [Fact]
    public void The_copy_script_fills_exactly_the_tables_schema_sql_creates()
    {
        var schema = File.ReadAllText(Path.Combine(Root, "Database", "schema.sql"));
        var created = Regex.Matches(schema, @"create table if not exists ([a-z_]+\.[a-z_]+)", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value).ToHashSet();

        var script = ReadScript();
        var filled = Regex.Matches(script, @"^insert into ([a-z_]+\.[a-z_]+)\s*\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        filled.Add("config.settings"); // updated in place (the seeded defaults are overwritten by hand-changed values)
        Assert.Contains("update config.settings t", script);

        var expected = created.Except(SeededBySchema).Except(NotInOldProject).Concat(CreatedByScript).ToHashSet();
        Assert.True(expected.SetEquals(filled),
            "Tables missing from copy-from-old.sql: [" + string.Join(", ", expected.Except(filled).OrderBy(x => x)) +
            "]; tables it fills that schema.sql does not create: [" + string.Join(", ", filled.Except(expected).OrderBy(x => x)) +
            "]. Update the script and test-copy.ps1 together with schema.sql.");
    }

    [Fact]
    public void The_copy_script_holds_no_host_or_password()
    {
        var script = ReadScript();
        Assert.DoesNotMatch(@"db\.[a-z0-9]{15,}\.supabase\.co", script);
        Assert.DoesNotMatch(@"password\s+'[^']+'", script);          // user mapping options use :'old_password'
        Assert.Contains("password :'old_password'", script);
        Assert.Contains("host :'old_host'", script);
    }
}
