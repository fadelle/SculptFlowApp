using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Omni.App.Controllers.Integrations;

namespace Omni.App.Tests.Guards;

/// <summary>
/// The contracts between the app and its three n8n workflows (docs/n8n/*.json, docs/system-design.html §7): each payload
/// record serializes to exactly the field list the design doc documents; each workflow reads only fields its trigger
/// sends; every call back into the app hits a real route and verb with the "Omni ingest key" Header Auth credential (never
/// a key in the file); the AI workflow's tools carry the new names and its result switches match the backend's codes; and
/// every node a workflow refers to exists.
/// </summary>
public class N8nContractTests
{
    private const string AppHost = "https://sculptflowapp.onrender.com"; // the base URL the workflow files carry (set at import)
    private const string IngestCredential = "Omni ingest key";
    private const string AiWorkflow = "omni-ai-assistant.json";
    private const string CalendarWorkflow = "omni-calendar-sync.json";
    private const string BenchmarkWorkflow = "omni-retrieval-benchmark.json";

    private static readonly string Root = LeftoverNameScanner.FindRepoRoot();
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<string> DesignDoc = new(() => File.ReadAllText(Path.Combine(Root, "docs", "system-design.html")));

    public static TheoryData<string, Type> Payloads => new()
    {
        { "ai-trigger", typeof(AiTriggerPayload) },
        { "calendar-sync", typeof(CalendarSyncTriggerPayload) },
        { "calendar-sync-callback", typeof(CalendarSyncCallbackRequest) },
        { "benchmark-generator", typeof(BenchmarkGeneratorPayload) },
        { "benchmark-chunk", typeof(BenchmarkSourceChunk) }
    };

    public static TheoryData<string> Workflows => new() { AiWorkflow, CalendarWorkflow, BenchmarkWorkflow };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Each_payload_serializes_to_the_field_list_the_design_doc_documents(string contract, Type payload)
    {
        Assert.Equal(Documented(contract), FieldsOf(payload));
    }

    [Fact]
    public void The_ai_workflow_reads_only_trigger_fields_and_uses_the_clients_profile()
    {
        var used = BodyFields(Text(AiWorkflow), "user_message");
        var sent = FieldsOf(typeof(AiTriggerPayload));

        Assert.Empty(used.Except(sent));
        // The prompt is generic: the business's profile, words, language and timezone come from the trigger, and the reply
        // falls back to the contact's stored language when a message has none.
        Assert.Empty(new[] { "clientId", "contactId", "conversationId", "messageText", "traceparent", "clientName", "aiProfile", "terms",
            "language", "contactLanguage", "timezone" }.Except(used));
    }

    [Fact]
    public void The_calendar_workflow_reads_only_trigger_fields_and_calls_back_with_the_callback_fields()
    {
        var text = Text(CalendarWorkflow);
        var used = BodyFields(text, "booking_change");

        Assert.Empty(used.Except(FieldsOf(typeof(CalendarSyncTriggerPayload))));
        Assert.Empty(new[] { "provider", "operation", "accessToken", "externalEventId", "scheduledStart", "scheduledEnd", "timezone",
            "eventTitle", "eventDescription", "dashboardLink" }.Except(used));

        var callback = Parameters(Node(Load(CalendarWorkflow), "sync_callback")).GetProperty("jsonBody").GetString()!;
        Assert.Equal(FieldsOf(typeof(CalendarSyncCallbackRequest)).Order(),
            Regex.Matches(callback, "\"([A-Za-z]+)\":").Select(m => m.Groups[1].Value).Order());
    }

    [Fact]
    public void The_benchmark_workflow_reads_only_trigger_fields()
    {
        var used = BodyFields(Text(BenchmarkWorkflow), "receive_chunks");

        Assert.Contains("generationId", used);
        Assert.Empty(used.Except(FieldsOf(typeof(BenchmarkGeneratorPayload))));
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_call_back_into_the_app_hits_a_real_route_with_the_ingest_key_credential(string file)
    {
        Assert.DoesNotContain("x-ingest-key", Text(file), StringComparison.OrdinalIgnoreCase);

        var calls = Nodes(Load(file))
            .Where(n => Parameters(n).TryGetProperty("url", out var url) && url.GetString()!.Contains(AppHost))
            .ToList();
        Assert.NotEmpty(calls);
        foreach (var node in calls)
        {
            var name = node.GetProperty("name").GetString();
            var p = Parameters(node);
            Assert.True(p.GetProperty("authentication").GetString() == "genericCredentialType"
                && p.GetProperty("genericAuthType").GetString() == "httpHeaderAuth", $"{name} must use the Header Auth credential.");
            Assert.Equal(IngestCredential, node.GetProperty("credentials").GetProperty("httpHeaderAuth").GetProperty("name").GetString());

            var verb = p.TryGetProperty("method", out var method) ? method.GetString()! : "GET";
            var path = RoutePath(p.GetProperty("url").GetString()!);
            Assert.True(Routes.Value.Any(r => r.Verb == verb && r.Pattern.IsMatch(path)), $"{name}: no {verb} route matches {path}.");
        }
    }

    [Fact]
    public void The_ai_workflow_has_the_new_tool_names_and_switches_on_the_backends_codes()
    {
        var workflow = Load(AiWorkflow);
        var nodes = Nodes(workflow).ToList();

        Assert.Equal(["get_available_slots", "get_my_bookings", "search_knowledge", "update_contact"],
            nodes.Where(n => n.GetProperty("type").GetString()!.EndsWith(".httpRequestTool")).Select(n => n.GetProperty("name").GetString()).Order());
        var names = nodes.Select(n => n.GetProperty("name").GetString()).ToHashSet();
        Assert.Subset(names, new HashSet<string?> { "schedule_booking", "cancel_booking", "handoff_to_human", "send_message" });

        var prompt = Parameters(Node(workflow, "AI Agent")).GetProperty("options").GetProperty("systemMessage").GetString()!;
        Assert.All(new[] { "search_knowledge", "get_available_slots", "get_my_bookings", "update_contact", "bookingId", "serviceId" },
            word => Assert.Contains(word, prompt));

        // update_contact only ever sends the contact's language (the AI can't touch other fields through it).
        var update = Parameters(Node(workflow, "update_contact"));
        Assert.Equal("PATCH", update.GetProperty("method").GetString());
        Assert.Equal(["preferredLanguage"], Regex.Matches(update.GetProperty("jsonBody").GetString()!, "\"([A-Za-z]+)\":").Select(m => m.Groups[1].Value));

        var codes = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(new AiTestController().ListCodes().Result).Value);
        Assert.Equal(Strings(codes.GetProperty("schedule")).Order(), SwitchCodes(nodes, "schedule_booking").Order());
        Assert.Equal(Strings(codes.GetProperty("cancel")).Order(), SwitchCodes(nodes, "cancel_booking").Order());
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_node_a_workflow_refers_to_exists_and_it_is_exported_inactive_without_sample_data(string file)
    {
        var workflow = Load(file);
        var names = Nodes(workflow).Select(n => n.GetProperty("name").GetString()!).ToHashSet();

        Assert.All(Regex.Matches(Text(file), @"\$\('([^']+)'\)").Select(m => m.Groups[1].Value), n => Assert.Contains(n, names));
        foreach (var source in workflow.GetProperty("connections").EnumerateObject())
        {
            Assert.Contains(source.Name, names);
            var targets = source.Value.EnumerateObject().SelectMany(kind => kind.Value.EnumerateArray())
                .SelectMany(branch => branch.EnumerateArray()).Select(t => t.GetProperty("node").GetString()!);
            Assert.All(targets, t => Assert.Contains(t, names));
        }
        Assert.False(workflow.GetProperty("active").GetBoolean());
        Assert.Empty(workflow.GetProperty("pinData").EnumerateObject());
        Assert.False(workflow.TryGetProperty("id", out _), "An exported id would overwrite the old workflow on import.");
        var webhook = Assert.Single(Nodes(workflow), n => n.GetProperty("type").GetString() == "n8n-nodes-base.webhook");
        Assert.Equal(webhook.GetProperty("webhookId").GetString(), Parameters(webhook).GetProperty("path").GetString());
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> Documented(string contract)
    {
        var match = Regex.Match(DesignDoc.Value, $"data-contract=\"{contract}\">([^<]+)</code>");
        Assert.True(match.Success, $"docs/system-design.html has no data-contract=\"{contract}\" field list.");
        return match.Groups[1].Value.Split(',').Select(f => f.Trim()).ToList();
    }

    /// <summary>The JSON property names a payload type serializes to (camelCase, every property, in order).</summary>
    private static IReadOnlyList<string> FieldsOf(Type type) =>
        JsonSerializer.SerializeToElement(RuntimeHelpers.GetUninitializedObject(type), type, Web).EnumerateObject().Select(p => p.Name).ToList();

    /// <summary>Fields a workflow reads from its webhook body: <c>$('webhook').item.json.body.x</c> and <c>$json.body.x</c>.</summary>
    private static HashSet<string> BodyFields(string text, string webhookNode) =>
        Regex.Matches(text, $@"(?:\$\('{webhookNode}'\)\.item\.json|\$json)\.body\.([A-Za-z]+)").Select(m => m.Groups[1].Value).ToHashSet();

    private static IEnumerable<string> SwitchCodes(IEnumerable<JsonElement> nodes, string tool) =>
        nodes.Where(n => n.GetProperty("type").GetString() == "n8n-nodes-base.switch")
            .SelectMany(n => Parameters(n).GetProperty("rules").GetProperty("values").EnumerateArray())
            .SelectMany(rule => rule.GetProperty("conditions").GetProperty("conditions").EnumerateArray())
            .Where(c => c.GetProperty("leftValue").GetString()!.Contains($"$('{tool}').item.json.code"))
            .Select(c => c.GetProperty("rightValue").GetString()!.TrimStart('='));

    private static IEnumerable<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!);

    /// <summary>The path part of an n8n URL (expressions become one segment): "/api/ai/conversations/{x}/handoff".</summary>
    private static string RoutePath(string url)
    {
        var path = url.TrimStart('=')[AppHost.Length..];
        path = Regex.Replace(path, @"\{\{.*?\}\}", "x");
        var query = path.IndexOf('?');
        return query < 0 ? path : path[..query];
    }

    private static readonly Lazy<IReadOnlyList<(string Verb, Regex Pattern)>> Routes = new(() =>
        typeof(AiController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(controller =>
            {
                var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
                return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
                    .SelectMany(a => a.HttpMethods.Select(verb => (verb, Template: Combine(prefix, a.Template))));
            })
            .Select(r => (r.verb, new Regex("^/" + Regex.Replace(Regex.Escape(r.Template), @"\\\{[^/]*?}", "[^/]+") + "$")))
            .ToList());

    private static string Combine(string prefix, string? template) =>
        string.IsNullOrEmpty(template) ? prefix : template.StartsWith('/') || template.StartsWith("~/") ? template.TrimStart('~', '/') : $"{prefix}/{template}";

    private static string Text(string file) => File.ReadAllText(Path.Combine(Root, "docs", "n8n", file));

    private static JsonElement Load(string file) => JsonDocument.Parse(Text(file)).RootElement;

    private static IEnumerable<JsonElement> Nodes(JsonElement workflow) => workflow.GetProperty("nodes").EnumerateArray();

    private static JsonElement Node(JsonElement workflow, string name) => Nodes(workflow).Single(n => n.GetProperty("name").GetString() == name);

    private static JsonElement Parameters(JsonElement node) => node.GetProperty("parameters");
}
